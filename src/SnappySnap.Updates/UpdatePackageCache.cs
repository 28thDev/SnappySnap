using SnappySnap.Core;

namespace SnappySnap.Updates;

internal sealed class UpdatePackageCache(ReleaseVerifier verifier, string currentVersion, string cacheRoot, int windowsBuild)
{
    public async Task<VerifiedCachedUpdate?> FindLatestVerifiedAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(cacheRoot)) return null;
        VerifiedCachedUpdate? newest = null;
        foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var manifest = await ReleaseVerifier.ReadBoundedAsync(Path.Combine(directory, "latest.json"), 65536, cancellationToken).ConfigureAwait(false);
                var signature = await ReleaseVerifier.ReadBoundedAsync(Path.Combine(directory, "latest.sig"), 64, cancellationToken).ConfigureAwait(false);
                var release = verifier.Read(manifest, signature, windowsBuild);
                if (ReleaseVerifier.ParseVersion(release.Version) <= ReleaseVerifier.ParseVersion(currentVersion)) continue;
                var candidate = new DownloadedUpdate(release, directory);
                await VerifyAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (newest is null || ReleaseVerifier.ParseVersion(release.Version) > ReleaseVerifier.ParseVersion(newest.Download.Release.Version))
                    newest = new(new UpdateOffer(release, manifest, signature), candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
            {
                // An interrupted or tampered cache entry is not an installable package.
            }
        }
        return newest;
    }

    public async Task<DownloadedUpdate> DownloadAsync(UpdateOffer offer,
        Func<UpdateRelease, CancellationToken, Task<Stream>> openSource,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var release = verifier.Read(offer.Manifest, offer.Signature, windowsBuild);
        if (ReleaseVerifier.ParseVersion(release.Version) <= ReleaseVerifier.ParseVersion(currentVersion))
            throw new InvalidDataException("The update is not newer than this application.");

        var directory = Path.Combine(cacheRoot, release.Version + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var partial = Path.Combine(directory, release.FileName + ".partial");
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!);
            UpdateInstallPolicy.RequireDownloadSpace(drive.AvailableFreeSpace, release.Size);
            await using var source = await openSource(release, cancellationToken).ConfigureAwait(false);
            await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 131072, true))
            {
                var buffer = new byte[131072];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("Source installer exceeds its signed size.");
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    progress?.Report(100d * total / release.Size);
                }
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                await ReleaseVerifier.VerifyPackageAsync(target, release, cancellationToken).ConfigureAwait(false);
            }
            await File.WriteAllBytesAsync(Path.Combine(directory, "latest.json"), offer.Manifest, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(directory, "latest.sig"), offer.Signature, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, Path.Combine(directory, release.FileName));
            return new DownloadedUpdate(release, directory);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public async Task VerifyAsync(DownloadedUpdate update, CancellationToken cancellationToken)
    {
        var release = verifier.Read(
            await ReleaseVerifier.ReadBoundedAsync(Path.Combine(update.Directory, "latest.json"), 65536, cancellationToken).ConfigureAwait(false),
            await ReleaseVerifier.ReadBoundedAsync(Path.Combine(update.Directory, "latest.sig"), 64, cancellationToken).ConfigureAwait(false), windowsBuild);
        if (release != update.Release || ReleaseVerifier.ParseVersion(release.Version) <= ReleaseVerifier.ParseVersion(currentVersion))
            throw new InvalidDataException("Cached release changed.");
        await using var file = File.Open(Path.Combine(update.Directory, release.FileName), FileMode.Open, FileAccess.Read, FileShare.Read);
        await ReleaseVerifier.VerifyPackageAsync(file, release, cancellationToken).ConfigureAwait(false);
    }
}
