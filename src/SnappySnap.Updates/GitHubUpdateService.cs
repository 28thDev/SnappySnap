using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SnappySnap.Core;

namespace SnappySnap.Updates;

public sealed class GitHubUpdateService : IUpdateService, IDisposable
{
    private readonly HttpClient _client;
    private readonly Uri _latestUri;
    private readonly Uri _downloadBase;
    private readonly ReleaseVerifier _verifier;
    private readonly UpdatePackageCache _cache;
    private readonly string _currentVersion;
    private readonly int _windowsBuild;
    private int _operationActive;

    private GitHubUpdateService(HttpClient client, Uri latestUri, Uri downloadBase, ReleaseVerifier verifier,
        string currentVersion, string cacheRoot, int windowsBuild)
    {
        if (latestUri.Scheme != Uri.UriSchemeHttps || downloadBase.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Update endpoints must use HTTPS.");
        _client = client;
        _latestUri = latestUri;
        _downloadBase = downloadBase;
        _verifier = verifier;
        _currentVersion = currentVersion;
        _windowsBuild = windowsBuild;
        _cache = new UpdatePackageCache(verifier, currentVersion, cacheRoot, windowsBuild);
    }

    public static GitHubUpdateService Production(ReleaseVerifier verifier, string currentVersion, string cacheRoot, int windowsBuild) =>
        new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(30) },
            new Uri("https://api.github.com/repos/28thDev/SnappySnap/releases/latest"),
            new Uri("https://github.com/28thDev/SnappySnap/releases/download/"),
            verifier, currentVersion, cacheRoot, windowsBuild);

    internal static GitHubUpdateService ForTests(HttpClient client, Uri latestUri, Uri downloadBase,
        ReleaseVerifier verifier, string currentVersion, string cacheRoot, int windowsBuild) =>
        new(client, latestUri, downloadBase, verifier, currentVersion, cacheRoot, windowsBuild);

    private void Enter(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0)
            throw new InvalidOperationException("An update operation is already running.");
    }

    private void Exit() => Volatile.Write(ref _operationActive, 0);

    public async Task<UpdateOffer?> CheckAsync(CancellationToken cancellationToken)
    {
        Enter(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var ct = timeout.Token;
            using var latest = await SendAsync(_latestUri, api: true, missingIsNoRelease: true, ct).ConfigureAwait(false);
            if (latest is null) return null;
            await using var latestStream = await latest.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var metadata = await ReleaseVerifier.ReadBoundedAsync(latestStream, 1024 * 1024, ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
            var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Missing release tag.");
            if (!tag.StartsWith('v')) throw new InvalidDataException("Invalid release tag.");
            var version = tag[1..];
            ReleaseVerifier.ParseVersion(version);
            var installerName = $"SnappySnap-Setup-{version}-x64.exe";
            var assets = root.GetProperty("assets").EnumerateArray().ToArray();
            foreach (var name in new[] { "latest.json", "latest.sig", installerName })
            {
                if (assets.Count(asset => asset.GetProperty("name").GetString() == name &&
                                          asset.GetProperty("state").GetString() == "uploaded") != 1)
                    throw new InvalidDataException($"GitHub release is missing asset '{name}'.");
            }
            var manifest = await ReadAssetAsync(tag, "latest.json", 65536, ct).ConfigureAwait(false);
            var signature = await ReadAssetAsync(tag, "latest.sig", 64, ct).ConfigureAwait(false);
            var release = _verifier.Read(manifest, signature, _windowsBuild);
            if (release.Version != version || release.FileName != installerName)
                throw new InvalidDataException("Signed catalog does not match the GitHub release tag.");
            return ReleaseVerifier.ParseVersion(version) > ReleaseVerifier.ParseVersion(_currentVersion)
                ? new UpdateOffer(release, manifest, signature) : null;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateSourceException("GitHub update check timed out.", UpdateCheckFailure.Transient, inner: ex);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("GitHub release metadata is invalid.", ex);
        }
        finally { Exit(); }
    }

    public async Task<DownloadedUpdate> DownloadAsync(UpdateOffer offer, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Enter(cancellationToken);
        HttpResponseMessage? response = null;
        try
        {
            return await _cache.DownloadAsync(offer, async (release, ct) =>
            {
                var uri = AssetUri("v" + release.Version, release.FileName);
                response = await SendAsync(uri, api: false, missingIsNoRelease: false, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The selected GitHub release is unavailable.");
                return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            }, progress, cancellationToken).ConfigureAwait(false);
        }
        finally { response?.Dispose(); Exit(); }
    }

    public Task VerifyAsync(DownloadedUpdate update, CancellationToken cancellationToken) => _cache.VerifyAsync(update, cancellationToken);
    public Task<VerifiedCachedUpdate?> FindCachedAsync(CancellationToken cancellationToken) => _cache.FindLatestVerifiedAsync(cancellationToken);

    private Uri AssetUri(string tag, string name) => new(_downloadBase, Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name));

    private async Task<byte[]> ReadAssetAsync(string tag, string name, int limit, CancellationToken ct)
    {
        using var response = await SendAsync(AssetUri(tag, name), api: false, missingIsNoRelease: false, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("GitHub release asset is unavailable.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await ReleaseVerifier.ReadBoundedAsync(stream, limit, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage?> SendAsync(Uri uri, bool api, bool missingIsNoRelease, CancellationToken ct)
    {
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Update redirect must use HTTPS.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("SnappySnap/" + _currentVersion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(api ? "application/vnd.github+json" : "application/octet-stream"));
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidDataException("GitHub update redirect has no destination.");
                uri = new Uri(uri, location);
                continue;
            }
            if (response.StatusCode == HttpStatusCode.NotFound && missingIsNoRelease) { response.Dispose(); return null; }
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                var retry = response.Headers.RetryAfter?.Date
                    ?? (response.Headers.RetryAfter?.Delta is { } delta ? DateTimeOffset.UtcNow + delta : null);
                if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
                    && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
                {
                    if (epoch >= DateTimeOffset.MinValue.ToUnixTimeSeconds() && epoch <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
                    {
                        var reset = DateTimeOffset.FromUnixTimeSeconds(epoch);
                        if (retry is null || reset > retry) retry = reset;
                    }
                }
                response.Dispose();
                throw new UpdateSourceException("GitHub rate limited the update check.", UpdateCheckFailure.RateLimited, retry);
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new UpdateSourceException($"GitHub update request failed with HTTP {(int)status}.",
                    (int)status >= 500 ? UpdateCheckFailure.Transient : UpdateCheckFailure.InvalidRelease);
            }
            return response;
        }
        throw new InvalidDataException("Too many GitHub update redirects.");
    }

    public void Dispose() => _client.Dispose();
}
