using SnappySnap.Core;
using SnappySnap.Infrastructure;
using Xunit;

namespace SnappySnap.IntegrationTests;

public sealed class InstallerIntegrationTests
{
    [Theory]
    [InlineData("on", false, true)]
    [InlineData("off", true, false)]
    [InlineData("preserve", false, false)]
    [InlineData("preserve", true, true)]
    public async Task Startup_choice_is_persisted_without_replacing_other_settings(string mode, bool previous, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root, root);
            using var logger = new FileLogger(paths.LogsPath);
            var store = new JsonSettingsStore(paths, logger);
            var settings = AppSettings.Defaults();
            settings.General.StartWithWindows = previous;
            settings.General.ShelfRecentCount = 37;
            settings.Hotkeys.RegionScreenshot = "Ctrl+Shift+F9";
            await store.SaveAsync(settings, CancellationToken.None);
            // Preserve mode must not normalize/rewrite a user's JSON during an upgrade.
            if (mode == "preserve") await File.AppendAllTextAsync(paths.SettingsPath, "\n\n");
            var originalBytes = await File.ReadAllBytesAsync(paths.SettingsPath);
            bool? registered = null;
            await StartupConfiguration.ApplyAsync(mode, store, enabled => registered = enabled);
            var loaded = await store.LoadAsync(CancellationToken.None);
            Assert.Equal(expected, registered);
            Assert.Equal(expected, loaded.General.StartWithWindows);
            Assert.Equal(37, loaded.General.ShelfRecentCount);
            Assert.Equal("Ctrl+Shift+F9", loaded.Hotkeys.RegionScreenshot);
            if (mode == "preserve") Assert.Equal(originalBytes, await File.ReadAllBytesAsync(paths.SettingsPath));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Registration_failure_is_reported_and_previous_setting_restored()
    {
        var store = new MemorySettings();
        store.Settings.General.StartWithWindows = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            StartupConfiguration.ApplyAsync("on", store, _ => throw new UnauthorizedAccessException()));
        Assert.False(store.Settings.General.StartWithWindows);
    }

    [Fact]
    public async Task Invalid_mode_does_not_load_or_write_profile()
    {
        var store = new MemorySettings();
        await Assert.ThrowsAsync<ArgumentException>(() => StartupConfiguration.ApplyAsync("invalid", store, _ => throw new InvalidOperationException()));
        Assert.Equal(0, store.LoadCount);
    }

    [Fact]
    public void Maintenance_blocks_application_but_allows_headless_configuration()
    {
        var scope = @"Local\SnappySnap.Tests." + Guid.NewGuid().ToString("N");
        ApplicationLifetimeGate? Enter(bool configuration, out string? error) => ApplicationLifetimeGate.TryEnter(configuration, out error, scope + ".Running", scope + ".Maintenance", scope + ".Handoff");
        using var maintenance = new Mutex(false, scope + ".Maintenance");
        Assert.Null(Enter(false, out var error));
        Assert.Contains("installed or removed", error);
        using var first = Enter(true, out error);
        Assert.NotNull(first);
        Assert.Null(error);
        Assert.Null(Enter(true, out error));
        Assert.Contains("already running", error);
    }

    [Fact]
    public void Update_handoff_blocks_launch_but_allows_installer_configuration()
    {
        var scope = @"Local\SnappySnap.Tests." + Guid.NewGuid().ToString("N");
        ApplicationLifetimeGate? Enter(bool configuration, out string? error) => ApplicationLifetimeGate.TryEnter(configuration, out error, scope + ".Running", scope + ".Maintenance", scope + ".Handoff");
        using var handoff = new Mutex(false, scope + ".Handoff");
        Assert.Null(Enter(false, out var error));
        Assert.NotNull(error);
        using var configuration = Enter(true, out error);
        Assert.NotNull(configuration); Assert.Null(error);
    }

    [Fact]
    public async Task Legacy_settings_gain_update_defaults_without_losing_existing_preferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root, root); paths.EnsureDirectories();
            using var logger = new FileLogger(paths.LogsPath);
            var store = new JsonSettingsStore(paths, logger);
            await File.WriteAllTextAsync(paths.SettingsPath, "{\"schemaVersion\":3,\"updates\":{\"automaticChecks\":false,\"sourceFolder\":\"C:\\\\old\"},\"general\":{\"startWithWindows\":false,\"shelfRecentCount\":37},\"screenshot\":{\"format\":\"Jpg\"}}");
            var settings = await store.LoadAsync(default);
            Assert.False(settings.Updates.AutomaticChecks); Assert.Equal(6, settings.SchemaVersion);
            settings.Updates.AutomaticChecks = false; await store.SaveAsync(settings, default);
            var loaded = await store.LoadAsync(default);
            Assert.False(loaded.General.StartWithWindows); Assert.Equal(37, loaded.General.ShelfRecentCount); Assert.Equal("Jpg", loaded.Screenshot.Format);
            Assert.False(loaded.Updates.AutomaticChecks);
            Assert.DoesNotContain("sourceFolder", await File.ReadAllTextAsync(paths.SettingsPath));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class MemorySettings : ISettingsStore
    {
        public AppSettings Settings { get; private set; } = AppSettings.Defaults();
        public int LoadCount { get; private set; }
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) { LoadCount++; return Task.FromResult(Settings); }
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) { Settings = settings; return Task.CompletedTask; }
    }
}
