using SnappySnap.Infrastructure;
using Xunit;

namespace SnappySnap.IntegrationTests;

public sealed class FileLoggerTests
{
    [Fact]
    public void Old_logs_are_pruned_and_a_locked_log_does_not_fail_the_caller()
    {
        var root = Path.Combine(Path.GetTempPath(), "SnappySnapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < 15; i++)
            {
                var old = Path.Combine(root, $"snappysnap-20250101-{i:000000000}.log");
                File.WriteAllText(old, "old");
                File.SetLastWriteTimeUtc(old, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i));
            }
            var unrelated = Path.Combine(root, "update-setup.log");
            File.WriteAllText(unrelated, "keep");

            using var logger = new FileLogger(root, maxBytes: 300);
            var remaining = Directory.GetFiles(root, "snappysnap-*.log").Select(Path.GetFileName).Order().ToArray();
            Assert.Equal(Enumerable.Range(5, 10).Select(i => $"snappysnap-20250101-{i:000000000}.log"), remaining);
            Assert.True(File.Exists(unrelated));

            for (var i = 0; i < 30; i++) logger.Info("Rotation entry " + i);
            Assert.InRange(Directory.GetFiles(root, "snappysnap-*.log").Length, 1, 11);

            var current = Directory.GetFiles(root, "snappysnap-????????.log").Single();
            using (new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.None))
                logger.Error("Written while the log is locked.", new IOException("ignored"));
            logger.Info("Logging resumes after the lock is released.");
            Assert.Contains("Logging resumes", File.ReadAllText(Directory.GetFiles(root, "snappysnap-????????.log").Single()));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
