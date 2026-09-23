using System.Text.Json;
using SnappySnap.Core;

namespace SnappySnap.Infrastructure;

public sealed class FileLogger : IAppLogger, IDisposable
{
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly long _maxBytes;
    private string _currentPath;

    public FileLogger(string directory, long maxBytes = 5 * 1024 * 1024)
    {
        _directory = directory;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(directory);
        _currentPath = Path.Combine(directory, $"snappysnap-{DateTime.UtcNow:yyyyMMdd}.log");
    }

    public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) => Write("Info", message, null, properties);
    public void Warn(string message, IReadOnlyDictionary<string, object?>? properties = null) => Write("Warning", message, null, properties);
    public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) => Write("Error", message, exception, properties);

    private void Write(string level, string message, Exception? exception, IReadOnlyDictionary<string, object?>? properties)
    {
        var entry = new Dictionary<string, object?>
        {
            ["timestampUtc"] = DateTimeOffset.UtcNow,
            ["level"] = level,
            ["message"] = message,
            ["appVersion"] = AppVersion.Display,
            ["osVersion"] = Environment.OSVersion.VersionString
        };
        if (exception is not null)
        {
            entry["exception"] = new { type = exception.GetType().FullName, message = exception.Message, stack = exception.StackTrace };
        }
        if (properties is not null)
        {
            foreach (var property in properties)
            {
                entry[property.Key] = property.Value;
            }
        }

        var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
        lock (_gate)
        {
            RotateIfNeeded(line.Length);
            File.AppendAllText(_currentPath, line);
        }
    }

    private void RotateIfNeeded(int incomingLength)
    {
        if (!File.Exists(_currentPath) || new FileInfo(_currentPath).Length + incomingLength <= _maxBytes)
        {
            return;
        }

        var rotated = Path.Combine(_directory, $"snappysnap-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.log");
        File.Move(_currentPath, rotated, overwrite: true);
    }

    public void Dispose()
    {
    }
}
