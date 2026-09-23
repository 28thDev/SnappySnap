using System.Security.Cryptography;
using System.Text.Json;
using SnappySnap.Core;
using SnappySnap.Infrastructure;
using SnappySnap.Updates;
using Xunit;
namespace SnappySnap.IntegrationTests;

public sealed class UpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SnappySnap-update-test-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private string Source => Path.Combine(_root, "source");
    private string Cache => Path.Combine(_root, "cache");
    private ReleaseVerifier Verifier => new(_key.ExportSubjectPublicKeyInfoPem());
    private LocalUpdateService Service => new(Verifier, "0.4.0", Source, Cache, 26100);
    public UpdateTests() { Directory.CreateDirectory(Source); }
    private async Task<UpdateRelease> Publish(string version = "0.4.1", string platform = "win-x64", string? name = null, int build = 22000)
    {
        var bytes = new byte[400000]; RandomNumberGenerator.Fill(bytes);
        var release = new UpdateRelease(version, DateTimeOffset.UtcNow, "Test release", name ?? $"SnappySnap-Setup-{version}-x64.exe", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), platform, build);
        if (name is null) await File.WriteAllBytesAsync(Path.Combine(Source, release.FileName), bytes);
        await WriteCatalog(release); return release;
    }
    private async Task WriteCatalog(UpdateRelease release)
    {
        var manifest = JsonSerializer.SerializeToUtf8Bytes(release, ReleaseVerifier.JsonOptions);
        await File.WriteAllBytesAsync(Path.Combine(Source, "latest.json"), manifest);
        await File.WriteAllBytesAsync(Path.Combine(Source, "latest.sig"), _key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
    [Fact] public async Task SignedUpdateCopiesAndVerifiesWithoutChangingSource()
    {
        var release = await Publish(); var original = await File.ReadAllBytesAsync(Path.Combine(Source, release.FileName));
        var service = Service; var offer = Assert.IsType<UpdateOffer>(await service.CheckAsync(default));
        var download = await service.DownloadAsync(offer, null, default); await service.VerifyAsync(download, default);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(download.Directory, release.FileName)));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(Source, release.FileName)));
        Assert.Empty(Directory.GetFiles(Cache, "*.partial", SearchOption.AllDirectories));
    }
    [Theory] [InlineData("0.4.0")] [InlineData("0.3.9")]
    public async Task EqualOrOlderIsNotOffered(string version) { await Publish(version); Assert.Null(await Service.CheckAsync(default)); }
    [Theory] [InlineData("0.4")] [InlineData("0.4.1-beta")] [InlineData("00.4.1")] [InlineData("-1.4.1")]
    public void NumericVersionRequired(string version) => Assert.Throws<InvalidDataException>(() => ReleaseVerifier.ParseVersion(version));
    [Fact] public void NumericComparisonIsNotLexical() => Assert.True(ReleaseVerifier.ParseVersion("0.10.0") > ReleaseVerifier.ParseVersion("0.9.9"));
    [Theory] [InlineData("win-arm64", 22000)] [InlineData("win-x64", 99999)]
    public async Task IncompatibleReleaseRejected(string platform, int build) { await Publish(platform: platform, build: build); await Assert.ThrowsAsync<InvalidDataException>(() => Service.CheckAsync(default)); }
    [Theory] [InlineData("../other.exe")] [InlineData("C:\\other.exe")] [InlineData("SnappySnap-Setup-0.4.1-x64.exe:stream")]
    public async Task UnsafeFilenameRejected(string name) { await Publish(name: name); await Assert.ThrowsAsync<InvalidDataException>(() => Service.CheckAsync(default)); }
    [Fact] public async Task TestKeyNeverTrustedByProduction()
    {
        await Publish();
        Assert.Throws<CryptographicException>(() => ReleaseVerifier.Production().Read(File.ReadAllBytes(Path.Combine(Source, "latest.json")), File.ReadAllBytes(Path.Combine(Source, "latest.sig")), 26100));
    }
    [Fact] public async Task ModifiedCatalogRejectedBeforeParsing()
    {
        await Publish(); await File.AppendAllTextAsync(Path.Combine(Source, "latest.json"), " ");
        await Assert.ThrowsAsync<CryptographicException>(() => Service.CheckAsync(default));
    }
    [Fact] public async Task CorruptPackageRejectedAndPartialRemoved()
    {
        var release = await Publish(); var offer = (await Service.CheckAsync(default))!;
        await File.WriteAllBytesAsync(Path.Combine(Source, release.FileName), new byte[release.Size]);
        await Assert.ThrowsAsync<CryptographicException>(() => Service.DownloadAsync(offer, null, default));
        Assert.Empty(Directory.GetFiles(Cache, "*.partial", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Cache, "*.exe", SearchOption.AllDirectories));
    }
    [Fact] public async Task WrongSizeRejected()
    {
        var release = await Publish(); var offer = (await Service.CheckAsync(default))!;
        await File.WriteAllBytesAsync(Path.Combine(Source, release.FileName), [1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Service.DownloadAsync(offer, null, default));
    }
    [Fact] public async Task CancellationRemovesOnlyCurrentPartialAndCanRetry()
    {
        await Publish(); var service = Service; var offer = (await service.CheckAsync(default))!;
        Directory.CreateDirectory(Cache); var keep = Path.Combine(Cache, "keep.txt"); await File.WriteAllTextAsync(keep, "retained");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(offer, new InlineProgress(_ => cancellation.Cancel()), cancellation.Token));
        Assert.Equal("retained", await File.ReadAllTextAsync(keep)); Assert.Empty(Directory.GetFiles(Cache, "*.partial", SearchOption.AllDirectories));
        await service.DownloadAsync(offer, null, default);
    }
    [Fact] public async Task CachedPackageIsRecheckedBeforeInstall()
    {
        await Publish(); var service = Service; var offer = (await service.CheckAsync(default))!;
        var download = await service.DownloadAsync(offer, null, default);
        await File.WriteAllBytesAsync(Path.Combine(download.Directory, download.Release.FileName), new byte[download.Release.Size]);
        await Assert.ThrowsAsync<CryptographicException>(() => service.VerifyAsync(download, default));
    }
    [Fact] public async Task MissingSourceIsAnExplicitError()
    {
        Directory.Delete(Source);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Service.CheckAsync(default));
    }
    [Fact] public async Task ConcurrentOperationRefused()
    {
        await Publish(); var service = Service; var offer = (await service.CheckAsync(default))!;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var copy = service.DownloadAsync(offer, new InlineProgress(_ => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); }), default);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckAsync(default)); }
        finally { release.Set(); await copy; }
    }
    [Theory] [InlineData(31)] [InlineData(32)] [InlineData(1)] [InlineData(999)]
    public void SetupErrorsNeverCountAsSuccess(int code) => Assert.Throws<InvalidOperationException>(() => UpdateInstallPolicy.RequireSuccessfulSetup(code, "0.4.1.0", "0.4.1"));
    [Fact] public void WrongInstalledVersionNeverCountsAsSuccess() => Assert.Throws<InvalidDataException>(() => UpdateInstallPolicy.RequireSuccessfulSetup(0, "0.4.0.0", "0.4.1"));
    [Fact] public void FolderBuildCannotSelfInstall() => Assert.Throws<InvalidOperationException>(() => UpdateInstallPolicy.RequireInstalledExecutable(Path.Combine(_root, "SnappySnap.exe")));
    [Fact] public void InsufficientSpaceIsReportedBeforeCopy() => Assert.Throws<IOException>(() => UpdateInstallPolicy.RequireDownloadSpace(10_000, 20_000));
    [Fact] public async Task PublishingCatalogIsNotReadHalfwayThrough()
    {
        await Publish();
        using var lease = File.Open(Path.Combine(Source, ".publish.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => Service.CheckAsync(default));
    }
    private sealed class InlineProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
    public void Dispose() { _key.Dispose(); Directory.Delete(_root, true); }
}
