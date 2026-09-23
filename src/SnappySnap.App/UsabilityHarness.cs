#if DEBUG
using System.Runtime.InteropServices;
using System.Windows.Input;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
using SnappySnap.Core;
using SnappySnap.Infrastructure;
namespace SnappySnap.App;

internal static class UsabilityHarness
{
    private static readonly double[] PreviewPositions = [.4, 2.4, 5.4];
    private static readonly int[] ExpectedHotkeyIds = [1002];

    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var logger = new FileLogger(Path.Combine(output, "logs"));
        await HotkeysAsync(logger);
        await File.WriteAllTextAsync(Path.Combine(output, "hotkeys.txt"), "PASS: real RegisterHotKey; Win32 conflict 1409; rollback retains previous set; normalized duplicates; swap; SendInput delivery; MOD_NOREPEAT. All test registrations released.\n");
        await PreviewAsync(output);
    }
    private static async Task PreviewAsync(string output)
    {
        const int sampleRate = 48000, seconds = 10;
        var wav = Path.Combine(output, "sync-markers.wav");
        using (var writer = new BinaryWriter(File.Create(wav)))
        {
            var length = sampleRate * seconds * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + length); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(length);
            for (var sample = 0; sample < sampleRate * seconds; sample++)
            {
                var time = sample / (double)sampleRate;
                var envelope = time % 1 < .15 ? Math.Sin(Math.PI * (time % 1) / .15) : 0;
                writer.Write((short)(6000 * envelope * Math.Sin(2 * Math.PI * 880 * time)));
            }
        }
        var composition = new MediaComposition();
        for (var i = 0; i < seconds; i++) composition.Clips.Add(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, (byte)(30 + i * 20), 70, 110), TimeSpan.FromSeconds(1)));
        composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(wav)));
        var folder = await StorageFolder.GetFolderFromPathAsync(output); var source = await folder.CreateFileAsync("sync-markers.mp4", CreationCollisionOption.ReplaceExisting);
        var failure = await composition.RenderToFileAsync(source, MediaTrimmingPreference.Precise, MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga));
        if (failure != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new InvalidOperationException("Marker fixture render: " + failure);
        var sourceClip = await MediaClip.CreateFromFileAsync(source); var sourceProperties = sourceClip.GetVideoEncodingProperties();
        var edit = new VideoEditSession(TimeSpan.FromSeconds(seconds)); edit.Delete(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        using var preview = new WindowsVideoPreviewSession { IsMuted = true };
        await preview.LoadAsync(source.Path, edit.ExportTimeline(), CancellationToken.None);
        var observations = new List<string>();
        foreach (var position in new[] { .4, 2.4, 6.4, 1.4, 5.4 })
        {
            preview.Pause(); preview.TakeFrame(); preview.Seek(TimeSpan.FromSeconds(position));
            PreviewFrame? frame = null; var deadline = DateTime.UtcNow.AddSeconds(8);
            var expectedRed = 30 + (int)Math.Floor(edit.ToSource(TimeSpan.FromSeconds(position)).TotalSeconds) * 20;
            while (DateTime.UtcNow < deadline)
            {
                var candidate = preview.TakeFrame();
                if (candidate is not null)
                {
                    var offset = ((candidate.Height / 2) * candidate.Width + candidate.Width / 2) * 4;
                    if (Math.Abs(candidate.Bgra32[offset + 2] - expectedRed) < 12) { frame = candidate; break; }
                }
                await Task.Delay(30);
            }
            if (frame is null) throw new InvalidOperationException($"Seek {position}: expected source red {expectedRed}; position {preview.Position}; error {preview.Error}");
            if (Math.Abs(frame.Width / (double)frame.Height - sourceProperties.Width / (double)sourceProperties.Height) > .01) throw new InvalidOperationException("Preview changed the source aspect ratio.");
            observations.Add($"PASS edited seek {position:0.0}s -> correct source marker {edit.ToSource(TimeSpan.FromSeconds(position)).TotalSeconds:0.0}s");
        }
        for (var i = 0; i < 30; i++) preview.Seek(TimeSpan.FromSeconds(i % 7 + .4));
        preview.Seek(TimeSpan.FromSeconds(3.4)); preview.Play(); await Task.Delay(600); preview.Pause();
        if (preview.Position.TotalSeconds < 3.4 || preview.Position.TotalSeconds > 4.5) throw new InvalidOperationException("Rapid seek / resume did not converge: " + preview.Position);
        if (preview.Error is not null) throw new InvalidOperationException(preview.Error);
        var edited = Path.Combine(output, "sync-markers-edited.mp4");
        await new WindowsMediaVideoEditingService().ExportAsync(source.Path, edited, edit.ExportTimeline(), null, CancellationToken.None);
        observations.Add("PASS rapid seek / Play / Pause converged; edited composition exported. Preview muted for machine test.");
        observations.Add("Frame checks run muted; see separate audio-offset reports for WASAPI timing. Subjective audible quality is not inferred from frame checks.");
        await File.WriteAllLinesAsync(Path.Combine(output, "preview.txt"), observations);
        try
        {
            await MeasureAudioAsync(output, "preview", preview);
            await preview.LoadAsync(source.Path, new VideoEditSession(TimeSpan.FromSeconds(seconds)).ExportTimeline(), CancellationToken.None);
            await MeasureAudioAsync(output, "original", preview);
            var exportedClip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(edited));
            await preview.LoadAsync(edited, new VideoEditSession(exportedClip.OriginalDuration).ExportTimeline(), CancellationToken.None);
            await MeasureAudioAsync(output, "export", preview);
        }
        catch (Exception ex) { preview.Pause(); await File.WriteAllTextAsync(Path.Combine(output, "audio-blocked.txt"), ex.ToString()); throw; }
    }
    private static async Task MeasureAudioAsync(string output, string label, IVideoPreviewSession preview)
    {
        using var meter = new LoopbackMeter();
        preview.IsMuted = false; preview.Volume = .2;
        var report = new List<string>();
        foreach (var position in PreviewPositions)
        {
            preview.Pause(); preview.Seek(TimeSpan.FromSeconds(position)); await Task.Delay(350);
            preview.TakeFrame(); meter.Read();
            var visual = new List<double>(); var audio = new List<double>(); int? red = null;
            var above = false; var start = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            preview.Play();
            while (System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency - start < 1.9)
            {
                if (preview.TakeFrame() is { } frame)
                {
                    var current = frame.Bgra32[((frame.Height / 2) * frame.Width + frame.Width / 2) * 4 + 2];
                    var now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
                    if (red.HasValue && Math.Abs(current - red.Value) > 10 && (visual.Count == 0 || now - visual[^1] > .25)) visual.Add(now);
                    red = current;
                }
                foreach (var packet in meter.Read())
                {
                    var signal = packet.Level > .002;
                    if (signal && !above && packet.Time > start) audio.Add(packet.Time);
                    above = signal;
                }
                await Task.Delay(10);
            }
            preview.Pause();
            if (visual.Count < 1 || audio.Count < 1) throw new InvalidOperationException($"No measurable markers after seek {position}: video={visual.Count}, audio={audio.Count}. Check endpoint mute/volume.");
            foreach (var videoTime in visual)
            {
                var offset = audio.MinBy(a => Math.Abs(a - videoTime)) - videoTime;
                report.Add($"seek={position:0.0}s video marker audio offset={offset * 1000:0.0}ms");
                if (Math.Abs(offset) > .1) { await File.WriteAllLinesAsync(Path.Combine(output, label + "-audio-offsets.txt"), report); throw new InvalidOperationException("Measured preview offset exceeds 100 ms."); }
            }
        }
        await Task.Delay(250); meter.Read(); await Task.Delay(150);
        if (meter.Read().Any(packet => packet.Level > .002)) throw new InvalidOperationException("Fixture audio continued after Pause.");
        preview.IsMuted = true;
        report.Add("PASS: Pause drained audible fixture output; no continued 880 Hz pulse after settling.");
        report.Add("PASS: software frame arrival vs WASAPI render endpoint pulses after repeated seek/Play/Pause <=100 ms. Does not measure acoustic speaker/display latency or subjective distortion.");
        await File.WriteAllLinesAsync(Path.Combine(output, label + "-audio-offsets.txt"), report);
    }
    private static async Task HotkeysAsync(IAppLogger logger)
    {
        var first = new HiddenHostWindow(); var second = new HiddenHostWindow(); first.Show(); first.Hide(); second.Show(); second.Hide();
        using var hotkeys = new GlobalHotkeyService(first, logger); using var occupied = new GlobalHotkeyService(second, logger);
        try
        {
            if (GetAsyncKeyState(0x11) < 0 || GetAsyncKeyState(0x10) < 0 || GetAsyncKeyState(0x12) < 0) throw new InvalidOperationException("Release physical modifier keys before running hotkey harness.");
            var initial = new Dictionary<int, string> { [1001] = "Ctrl+Shift+F20", [1002] = "Ctrl+Shift+F21", [1003] = "Ctrl+Shift+F23" };
            if (hotkeys.Apply(initial).Any(r => !r.Registered)) throw new InvalidOperationException("Test shortcuts are occupied.");
            if (occupied.Apply(new Dictionary<int, string> { [1001] = "Ctrl+Shift+F22" }).Any(r => !r.Registered)) throw new InvalidOperationException("Conflict fixture shortcut is occupied.");
            var conflict = new Dictionary<int, string>(initial) { [1001] = "Ctrl+Shift+F22" };
            if (hotkeys.Apply(conflict).Single(r => r.Id == 1001).WindowsError != 1409) throw new InvalidOperationException("Expected Windows hotkey conflict.");
            if (!hotkeys.Snapshot().OrderBy(x => x.Key).SequenceEqual(initial.OrderBy(x => x.Key))) throw new InvalidOperationException("Working registrations were not restored.");
            var duplicate = new Dictionary<int, string>(initial) { [1002] = "shift+control+f20" };
            if (hotkeys.Apply(duplicate).All(r => r.Registered)) throw new InvalidOperationException("Normalized duplicate was accepted.");
            var swap = new Dictionary<int, string>(initial) { [1001] = initial[1002], [1002] = initial[1001] };
            if (hotkeys.Apply(swap).Any(r => !r.Registered)) throw new InvalidOperationException("Swap failed.");
            var delivered = new List<int>(); hotkeys.HotkeyPressed += (_, id) => delivered.Add(id);
            try
            {
                Send(0x11, false); Send(0x10, false); Send(0x83, false); // F20
                await Task.Delay(100);
                for (var i = 0; i < 4; i++) { Send(0x83, false); await Task.Delay(30); }
            }
            finally { Send(0x83, true); Send(0x10, true); Send(0x11, true); }
            await Task.Delay(100);
            if (!delivered.SequenceEqual(ExpectedHotkeyIds)) throw new InvalidOperationException("WM_HOTKEY delivery / no-repeat failed: " + string.Join(",", delivered));
        }
        finally { first.Close(); second.Close(); }
    }
    private static void Send(ushort key, bool up)
    {
        var input = new Input { Type = 1, Keyboard = new KeyboardInput { Key = key, Flags = up ? 2u : 0u } };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1) throw new InvalidOperationException("SendInput failed: " + Marshal.GetLastWin32Error());
    }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
#endif

