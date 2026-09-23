using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnappySnap.Core;
using SnappySnap.Editor;
using Xunit;
namespace SnappySnap.Editor.Tests;
public sealed class ImageExportTests
{
    [Fact]
    public async Task New_copy_does_not_replace_a_file_claimed_after_naming()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnappySnap-export-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "capture.png");
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 1, 2, 3, 255 }, 4);
        source.Freeze();
        var existing = new byte[] { 7, 8, 9 };
        try
        {
            await File.WriteAllBytesAsync(path, existing);
            await Assert.ThrowsAsync<IOException>(() => EditorRenderer.SaveBitmapAsync(source, path, "Png", default, overwriteExisting: false));
            Assert.Equal(existing, await File.ReadAllBytesAsync(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Png_optimization_preserves_every_color_and_real_transparency(bool transparent)
    {
        var pixels = new byte[257 * 23 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(i % 251); pixels[i + 1] = (byte)(i % 239); pixels[i + 2] = (byte)(i % 229);
            pixels[i + 3] = transparent && i % 7 == 0 ? (byte)127 : (byte)255;
        }
        var source = BitmapSource.Create(257, 23, 96, 96, PixelFormats.Bgra32, null, pixels, 257 * 4); source.Freeze();
        var path = Path.Combine(Path.GetTempPath(), "SnappySnap-png-" + Guid.NewGuid() + ".png");
        try
        {
            await EditorRenderer.SaveBitmapAsync(source, path, "Png", default);
            var saved = EditorRenderer.LoadImage(path);
            Assert.Equal(pixels, EditorRenderer.ToCapturedImage(saved).Bgra32);
            Assert.Equal(transparent ? 6 : 2, (await File.ReadAllBytesAsync(path))[25]); // PNG IHDR: RGBA or RGB.
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData("Png")]
    [InlineData("Jpg")]
    public async Task Failed_or_cancelled_replacement_keeps_existing_file_and_cleans_owned_temporary(string format)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnappySnap-export-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "capture." + format.ToLowerInvariant());
        var document = new EditorDocument(new CapturedImage(40, 30, new byte[40 * 30 * 4]));
        try
        {
            await EditorRenderer.SaveAsync(document, path, format, default);
            var original = await File.ReadAllBytesAsync(path);
            document.Elements.Add(new RectangleElement(new Rect(1, 1, 20, 20)));
            using (var reader = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failure = await Record.ExceptionAsync(() => EditorRenderer.SaveAsync(document, path, format, default));
                Assert.True(failure is IOException or UnauthorizedAccessException, $"Expected a Windows file-sharing error, got {failure}");
            }
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EditorRenderer.SaveAsync(document, path, format, new CancellationToken(true)));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            Assert.Single(Directory.GetFiles(directory));
            await EditorRenderer.SaveAsync(document, path, format, default);
            var replaced = await File.ReadAllBytesAsync(path);
            Assert.False(original.SequenceEqual(replaced));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task SelectionAdornersNeverChangeExportedPixels()
    {
        var document = new EditorDocument(new CapturedImage(80, 60, new byte[80 * 60 * 4]));
        var rectangle = new RectangleElement(new Rect(10, 10, 30, 25)) { IsSelected = true }; document.Elements.Add(rectangle);
        var directory = Path.Combine(Path.GetTempPath(), "SnappySnap-export-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var selected = Path.Combine(directory, "selected.png"); var plain = Path.Combine(directory, "plain.png");
        try
        {
            await EditorRenderer.SavePngAsync(document, selected, CancellationToken.None); rectangle.IsSelected = false;
            await EditorRenderer.SavePngAsync(document, plain, CancellationToken.None);
            Assert.Equal(await File.ReadAllBytesAsync(selected), await File.ReadAllBytesAsync(plain));
        }
        finally { File.Delete(selected); File.Delete(plain); Directory.Delete(directory); }
    }
    [Theory] [InlineData("Png")] [InlineData("Jpg")]
    public async Task EncoderMatchesExtensionCropAndWhiteJpegBackground(string format)
    {
        var document = new EditorDocument(new CapturedImage(40, 30, new byte[40 * 30 * 4]));
        var history = new EditorCommandHistory(); history.Execute(new CropCommand(Rect.Empty, new Rect(5, 5, 20, 10)), document);
        var file = Path.Combine(Path.GetTempPath(), "SnappySnap-test-" + Guid.NewGuid() + "." + format.ToLowerInvariant());
        try
        {
            await EditorRenderer.SaveAsync(document, file, format, CancellationToken.None);
            using var stream = File.OpenRead(file); var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(20, decoder.Frames[0].PixelWidth); Assert.Equal(10, decoder.Frames[0].PixelHeight);
            if (format == "Jpg")
            {
                Assert.IsType<JpegBitmapDecoder>(decoder); var image = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
                var bytes = new byte[20 * 10 * 4]; image.CopyPixels(bytes, 80, 0); Assert.All(bytes, b => Assert.InRange(b, (byte)250, (byte)255));
            }
            else Assert.IsType<PngBitmapDecoder>(decoder);
        }
        finally { File.Delete(file); }
    }
}
