using System.Diagnostics;
using System.IO.Pipes;
using SnappySnap.Core;
using SnappySnap.Updates;
namespace SnappySnap.Infrastructure;

public sealed class UpdateHandoff : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly StreamWriter _writer;
    private readonly Process _host;
    private UpdateHandoff(NamedPipeServerStream pipe, StreamWriter writer, Process host) { _pipe = pipe; _writer = writer; _host = host; }
    public static async Task<UpdateHandoff> StartAsync(DownloadedUpdate update)
    {
        UpdateInstallPolicy.RequireInstalledExecutable(Environment.ProcessPath);
        var nonce = Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream("SnappySnap.Update." + nonce, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Process? host = null;
        try
        {
            var hostDirectory = Path.Combine(update.Directory, "host-" + nonce);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(hostDirectory);
                File.Copy(Path.Combine(AppContext.BaseDirectory, "UpdateHost", "SnappySnap.UpdateHost.exe"), Path.Combine(hostDirectory, "SnappySnap.UpdateHost.exe"));
            });
            var start = new ProcessStartInfo(Path.Combine(hostDirectory, "SnappySnap.UpdateHost.exe")) { UseShellExecute = false, WorkingDirectory = hostDirectory };
            start.Environment["SNAPPYSNAP_LANGUAGE"] = SnappySnap.Localization.L.Current.Language;
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)); start.ArgumentList.Add(nonce); start.ArgumentList.Add(update.Directory);
            host = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            if (await reader.ReadLineAsync(timeout.Token) != "READY") throw new IOException("Update helper did not confirm readiness.");
            return new UpdateHandoff(pipe, new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true }, host);
        }
        catch { pipe.Dispose(); host?.Dispose(); throw; }
    }
    public Task CommitAsync() => _writer.WriteLineAsync("COMMIT");
    public async ValueTask DisposeAsync() { await _writer.DisposeAsync(); await _pipe.DisposeAsync(); _host.Dispose(); }
}
