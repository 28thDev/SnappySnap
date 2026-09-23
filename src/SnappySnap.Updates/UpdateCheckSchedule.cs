using System.Text.Json;
using SnappySnap.Core;

namespace SnappySnap.Updates;

public sealed class UpdateCheckSchedule(string path, IAppLogger logger, TimeProvider? clock = null) : IDisposable
{
    private sealed class State
    {
        public DateTimeOffset? LastAttemptUtc { get; set; }
        public DateTimeOffset? NextAutomaticCheckUtc { get; set; }
        public int ConsecutiveFailures { get; set; }
    }

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private State _state = new();

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 4096) throw new InvalidDataException("Update schedule is too large.");
                var loaded = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
                _state = loaded ?? throw new InvalidDataException("Update schedule is empty.");
                _state.ConsecutiveFailures = Math.Clamp(_state.ConsecutiveFailures, 0, 3);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                logger.Warn("Update check schedule could not be read; automatic checks will wait a day.",
                    new Dictionary<string, object?> { ["error"] = ex.Message });
                _state = new State { NextAutomaticCheckUtc = _clock.GetUtcNow().AddDays(1) };
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> BeginAutomaticAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _clock.GetUtcNow();
            if (_state.NextAutomaticCheckUtc is { } next && next > now) return false;
            _state.LastAttemptUtc = now;
            _state.NextAutomaticCheckUtc = now.AddDays(1); // Persist before the request to bound rapid-restart retries.
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task RecordAsync(UpdateCheckOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome.Failure is UpdateCheckFailure.Skipped or UpdateCheckFailure.Cancelled) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _clock.GetUtcNow();
            _state.LastAttemptUtc = now;
            if (outcome.Failure == UpdateCheckFailure.None)
            {
                _state.ConsecutiveFailures = 0;
                _state.NextAutomaticCheckUtc = now.AddDays(1);
            }
            else if (outcome.Failure == UpdateCheckFailure.Transient)
            {
                _state.ConsecutiveFailures = Math.Min(_state.ConsecutiveFailures + 1, 3);
                _state.NextAutomaticCheckUtc = now.Add(_state.ConsecutiveFailures switch
                {
                    1 => TimeSpan.FromHours(1),
                    2 => TimeSpan.FromHours(6),
                    _ => TimeSpan.FromDays(1)
                });
            }
            else if (outcome.Failure == UpdateCheckFailure.RateLimited)
            {
                _state.ConsecutiveFailures = Math.Min(_state.ConsecutiveFailures + 1, 3);
                var minimum = now.AddHours(1);
                _state.NextAutomaticCheckUtc = outcome.RetryAfterUtc is { } retry && retry > minimum
                    ? retry : outcome.RetryAfterUtc is not null ? minimum : now.AddDays(1);
            }
            else
            {
                _state.ConsecutiveFailures = Math.Min(_state.ConsecutiveFailures + 1, 3);
                _state.NextAutomaticCheckUtc = now.AddDays(1);
            }
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_state), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose() => _gate.Dispose();
}
