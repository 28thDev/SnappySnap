using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using SnappySnap.Updates;
using SnappySnap.Localization;

namespace SnappySnap.UpdateHost;
internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);

    [STAThread]
    private static int Main(string[] args)
    {
        L.SetLanguage(Environment.GetEnvironmentVariable("SNAPPYSNAP_LANGUAGE") == "ru" ? "ru" : "en");
        try { RunAsync(args).GetAwaiter().GetResult(); return 0; }
        catch (Exception ex)
        {
            if (MessageBoxW(0, L.F("Update did not finish. Verified installer and logs are available in:\n{0}", UpdateInstallPolicy.CacheRoot) + "\n\n" + ex.Message,
                    L.T("SnappySnap — Update"), 0x10) == 0)
                Debug.WriteLine($"Could not show the update error dialog (Win32 error {Marshal.GetLastWin32Error()}).");
            return 1;
        }
    }
    private static async Task RunAsync(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[0], out var parentId) || !Guid.TryParseExact(args[1], "N", out _)) throw new ArgumentException("Invalid update handoff.");
        using var parent = Process.GetProcessById(parentId);
        UpdateInstallPolicy.RequireInstalledExecutable(parent.MainModule?.FileName);
        var directory = Path.GetFullPath(args[2]);
        if (!directory.StartsWith(UpdateInstallPolicy.CacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update is outside the application cache.");
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        using var handoff = new Mutex(false, @"Global\SnappySnap.UpdateHandoff." + sid, out var created);
        if (!created) throw new InvalidOperationException("Another update is already being prepared.");
        using var pipe = new NamedPipeClientStream(".", "SnappySnap.Update." + args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await pipe.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync("READY");
        // EOF, cancellation or a missing commit never authorizes installation.
        using var commitTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        if (await reader.ReadLineAsync(commitTimeout.Token) != "COMMIT") return;
        using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await parent.WaitForExitAsync(exitTimeout.Token);
        var resultPath = Path.Combine(directory, "result.json");
        var logPath = Path.Combine(directory, "setup.log");
        try
        {
            var verifier = ReleaseVerifier.Production();
            var release = verifier.Read(await ReleaseVerifier.ReadBoundedAsync(Path.Combine(directory, "latest.json"), 65536, CancellationToken.None),
                await ReleaseVerifier.ReadBoundedAsync(Path.Combine(directory, "latest.sig"), 64, CancellationToken.None), Environment.OSVersion.Version.Build);
            var installed = FileVersionInfo.GetVersionInfo(UpdateInstallPolicy.InstalledExecutable).FileVersion?.Trim() ?? "";
            if (Version.Parse(release.Version + ".0") <= Version.Parse(installed)) throw new InvalidOperationException("The installed version is already equal or newer.");
            var setup = Path.Combine(directory, release.FileName);
            // Hold the verified bytes read-only until Setup exits, preventing replacement between verification and execution.
            await using var package = new FileStream(setup, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
            await ReleaseVerifier.VerifyPackageAsync(package, release, CancellationToken.None);
            if (FileVersionInfo.GetVersionInfo(setup).FileVersion?.Trim() != release.Version + ".0") throw new InvalidDataException("Setup resource version differs from its catalog.");
            var start = new ProcessStartInfo(setup) { UseShellExecute = false, WorkingDirectory = directory };
            foreach (var value in new[] { "/SILENT", "/NORESTART", "/RESTARTEXITCODE=32", "/NOCLOSEAPPLICATIONS", "/NOFORCECLOSEAPPLICATIONS", "/LOG=" + logPath }) start.ArgumentList.Add(value);
            using var installer = Process.Start(start) ?? throw new IOException("Could not start Setup.");
            await installer.WaitForExitAsync();
            UpdateInstallPolicy.RequireSuccessfulSetup(installer.ExitCode, FileVersionInfo.GetVersionInfo(UpdateInstallPolicy.InstalledExecutable).FileVersion ?? "", release.Version);
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { success = true, version = release.Version, timestamp = DateTimeOffset.UtcNow }, ReleaseVerifier.JsonOptions));
            handoff.Dispose(); // Release before normal startup; Setup has already released maintenance.
            Process.Start(new ProcessStartInfo(UpdateInstallPolicy.InstalledExecutable) { UseShellExecute = false, ArgumentList = { "--tray" } });
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { success = false, error = ex.Message, logPath, timestamp = DateTimeOffset.UtcNow }, ReleaseVerifier.JsonOptions));
            throw;
        }
    }
}
