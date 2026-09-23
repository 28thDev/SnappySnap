namespace SnappySnap.Core;

public sealed class UpdateSettings
{
    public bool AutomaticChecks { get; set; } = true;
}
public sealed record UpdateRelease(string Version, DateTimeOffset PublishedUtc, string Notes,
    string FileName, long Size, string Sha256, string Platform, int MinimumWindowsBuild);
public sealed record UpdateOffer(UpdateRelease Release, byte[] Manifest, byte[] Signature);
public sealed record DownloadedUpdate(UpdateRelease Release, string Directory);
public sealed record VerifiedCachedUpdate(UpdateOffer Offer, DownloadedUpdate Download);
public interface IUpdateService
{
    Task<UpdateOffer?> CheckAsync(CancellationToken cancellationToken);
    Task<DownloadedUpdate> DownloadAsync(UpdateOffer offer, IProgress<double>? progress, CancellationToken cancellationToken);
    Task VerifyAsync(DownloadedUpdate update, CancellationToken cancellationToken);
}
public enum UpdateCheckFailure { None, Transient, RateLimited, InvalidRelease, Cancelled, Skipped }
public sealed record UpdateCheckOutcome(UpdateCheckFailure Failure, DateTimeOffset? RetryAfterUtc = null);
public sealed class UpdateSourceException(string message, UpdateCheckFailure failure, DateTimeOffset? retryAfterUtc = null, Exception? inner = null)
    : IOException(message, inner)
{
    public UpdateCheckFailure Failure { get; } = failure;
    public DateTimeOffset? RetryAfterUtc { get; } = retryAfterUtc;
}
public enum ExitPreparationResult { Ready, Busy, Cancelled, Error }
