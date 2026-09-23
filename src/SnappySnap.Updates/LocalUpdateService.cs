using SnappySnap.Core;

namespace SnappySnap.Updates;

// Retained for isolated update tests. Production uses the fixed GitHub source.
public sealed class LocalUpdateService(ReleaseVerifier verifier, string currentVersion, string sourceFolder, string cacheRoot, int windowsBuild) : IUpdateService
{
    private readonly UpdatePackageCache _cache = new(verifier, currentVersion, cacheRoot, windowsBuild);
    private int _operationActive;

    private void EnterOperation(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0)
            throw new InvalidOperationException("An update operation is already running.");
    }

    private void ExitOperation() => Volatile.Write(ref _operationActive, 0);

    public async Task<UpdateOffer?> CheckAsync(CancellationToken cancellationToken)
    {
        EnterOperation(cancellationToken);
        try
        {
            if (!Path.IsPathFullyQualified(sourceFolder)) throw new InvalidDataException("Choose an absolute release folder.");
            // Hold the publisher lock read-only while taking a consistent catalog snapshot.
            var publicationLock = Path.Combine(sourceFolder, ".publish.lock");
            using var catalogLease = File.Exists(publicationLock) ? File.Open(publicationLock, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            var bytes = await ReleaseVerifier.ReadBoundedAsync(Path.Combine(sourceFolder, "latest.json"), 65536, cancellationToken).ConfigureAwait(false);
            var signature = await ReleaseVerifier.ReadBoundedAsync(Path.Combine(sourceFolder, "latest.sig"), 64, cancellationToken).ConfigureAwait(false);
            var release = verifier.Read(bytes, signature, windowsBuild);
            return ReleaseVerifier.ParseVersion(release.Version) > ReleaseVerifier.ParseVersion(currentVersion)
                ? new UpdateOffer(release, bytes, signature) : null;
        }
        finally { ExitOperation(); }
    }

    public async Task<DownloadedUpdate> DownloadAsync(UpdateOffer offer, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        EnterOperation(cancellationToken);
        try
        {
            return await _cache.DownloadAsync(offer, (release, _) =>
            {
                Stream stream = new FileStream(Path.Combine(sourceFolder, release.FileName), FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
                return Task.FromResult(stream);
            }, progress, cancellationToken).ConfigureAwait(false);
        }
        finally { ExitOperation(); }
    }

    public Task VerifyAsync(DownloadedUpdate update, CancellationToken cancellationToken) => _cache.VerifyAsync(update, cancellationToken);
}
