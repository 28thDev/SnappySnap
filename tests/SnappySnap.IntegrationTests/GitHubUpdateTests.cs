using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SnappySnap.Core;
using SnappySnap.Updates;
using Xunit;

namespace SnappySnap.IntegrationTests;

public sealed class GitHubUpdateTests : IDisposable
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "SnappySnap-github-test-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _installer = RandomNumberGenerator.GetBytes(1024);
    private readonly Uri _api = new("https://api.github.test/repos/28thDev/SnappySnap/releases/latest");
    private readonly Uri _base = new("https://github.test/28thDev/SnappySnap/releases/download/");
    private static string Name => "SnappySnap-Setup-0.7.0-x64.exe";
    private UpdateRelease Release { get; }
    private readonly byte[] _manifest;
    private readonly byte[] _signature;
    public GitHubUpdateTests()
    {
        Release = new("0.7.0", DateTimeOffset.UtcNow, "Update notes", Name, _installer.Length,
        Convert.ToHexString(SHA256.HashData(_installer)), "win-x64", 22000);
        _manifest = JsonSerializer.SerializeToUtf8Bytes(Release, ReleaseVerifier.JsonOptions);
        _signature = _key.SignData(_manifest, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private GitHubUpdateService Service(Handler handler) => GitHubUpdateService.ForTests(
        new HttpClient(handler), _api, _base, new ReleaseVerifier(_key.ExportSubjectPublicKeyInfoPem()),
        "0.6.1", _root, 26100);

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static byte[] Metadata(string tag = "v0.7.0", bool draft = false, bool prerelease = false, bool installer = true)
    {
        var names = installer ? new[] { "latest.json", "latest.sig", $"SnappySnap-Setup-{tag[1..]}-x64.exe" } : new[] { "latest.json", "latest.sig" };
        return JsonSerializer.SerializeToUtf8Bytes(new { tag_name = tag, draft, prerelease,
            assets = names.Select(name => new { name, state = "uploaded" }) });
    }
    private Handler FixtureHandler(byte[]? metadata = null, byte[]? manifest = null, byte[]? signature = null, byte[]? installer = null) => new(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/latest", StringComparison.Ordinal)) return Bytes(metadata ?? Metadata());
        if (path.EndsWith("/latest.json", StringComparison.Ordinal)) return Bytes(manifest ?? _manifest);
        if (path.EndsWith("/latest.sig", StringComparison.Ordinal)) return Bytes(signature ?? _signature);
        if (path.EndsWith(".exe", StringComparison.Ordinal)) return Bytes(installer ?? _installer);
        return new(HttpStatusCode.NotFound);
    });

    [Fact] public async Task StableTaggedReleaseDownloadsAndVerifiesOffline()
    {
        var handler = FixtureHandler(); using var service = Service(handler);
        var offer = Assert.IsType<UpdateOffer>(await service.CheckAsync(default));
        var download = await service.DownloadAsync(offer, null, default);
        await service.VerifyAsync(download, default);
        Assert.All(handler.Requests.Skip(1), uri => Assert.StartsWith(_base + "v0.7.0/", uri.ToString()));
        handler.Requests.Clear();
        await service.VerifyAsync(download, default);
        Assert.Empty(handler.Requests);
        using var offline = Service(new Handler(_ => throw new InvalidOperationException("Network must not be used.")));
        var restored = Assert.IsType<VerifiedCachedUpdate>(await offline.FindCachedAsync(default));
        Assert.Equal(download, restored.Download);
        await offline.VerifyAsync(restored.Download, default);
    }

    [Theory] [InlineData(true, false)] [InlineData(false, true)]
    public async Task DraftAndPrereleaseAreIgnored(bool draft, bool prerelease)
    {
        var handler = FixtureHandler(metadata: Metadata(draft: draft, prerelease: prerelease));
        using var service = Service(handler);
        Assert.Null(await service.CheckAsync(default));
        Assert.Single(handler.Requests);
    }

    [Fact] public async Task InstalledVersionIsNotOffered()
    {
        var installed = Release with { Version = "0.6.1", FileName = "SnappySnap-Setup-0.6.1-x64.exe" };
        var manifest = JsonSerializer.SerializeToUtf8Bytes(installed, ReleaseVerifier.JsonOptions);
        var signature = _key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        using var service = Service(FixtureHandler(metadata: Metadata(tag: "v0.6.1"), manifest: manifest, signature: signature));
        Assert.Null(await service.CheckAsync(default));
    }

    [Fact] public async Task MissingInstallerAndTagMismatchAreRejected()
    {
        using (var service = Service(FixtureHandler(metadata: Metadata(installer: false))))
            await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(default));
        using (var service = Service(FixtureHandler(metadata: Metadata(tag: "v0.7.1"))))
            await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(default));
    }

    [Fact] public async Task BadSignatureAndInstallerAreRejected()
    {
        using (var service = Service(FixtureHandler(signature: new byte[64])))
            await Assert.ThrowsAsync<CryptographicException>(() => service.CheckAsync(default));
        using (var service = Service(FixtureHandler(installer: new byte[_installer.Length])))
        {
            var offer = (await service.CheckAsync(default))!;
            await Assert.ThrowsAsync<CryptographicException>(() => service.DownloadAsync(offer, null, default));
        }
    }

    [Fact] public async Task NotFoundIsNoReleaseAndRateLimitCarriesRetryTime()
    {
        using (var service = Service(new Handler(_ => new(HttpStatusCode.NotFound))))
            Assert.Null(await service.CheckAsync(default));
        var retry = DateTimeOffset.UtcNow.AddHours(5);
        using (var service = Service(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retry);
            return response;
        })))
        {
            var error = await Assert.ThrowsAsync<UpdateSourceException>(() => service.CheckAsync(default));
            Assert.Equal(UpdateCheckFailure.RateLimited, error.Failure);
            Assert.Equal(retry, error.RetryAfterUtc);
        }
    }

    [Fact] public async Task RedirectToHttpAndExcessRedirectsAreRejected()
    {
        using (var service = Service(new Handler(_ => new(HttpStatusCode.Redirect)
            { Headers = { Location = new Uri("http://unsafe.test/latest.json") } })))
            await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(default));
        using (var service = Service(new Handler(_ => new(HttpStatusCode.Redirect)
            { Headers = { Location = new Uri("https://github.test/again") } })))
            await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(default));
    }

    [Fact] public async Task CancellationStopsCheck()
    {
        var handler = FixtureHandler(); using var service = Service(handler);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckAsync(cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact] public async Task HttpTimeoutIsARecoverableFailure()
    {
        using var client = new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(30) };
        using var service = GitHubUpdateService.ForTests(client, _api, _base,
            new ReleaseVerifier(_key.ExportSubjectPublicKeyInfoPem()), "0.6.1", _root, 26100);
        var error = await Assert.ThrowsAsync<UpdateSourceException>(() => service.CheckAsync(default));
        Assert.Equal(UpdateCheckFailure.Transient, error.Failure);
    }

    public void Dispose()
    {
        _key.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
