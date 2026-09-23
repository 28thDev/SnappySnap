using SnappySnap.Core;
using Xunit;

namespace SnappySnap.Core.Tests;

public sealed class FrozenDesktopSnapshotTests
{
    [Fact]
    public void Crop_supports_negative_virtual_coordinates_and_preserves_bgra_rows()
    {
        var source = CreateImage(4, 3);
        var snapshot = new FrozenDesktopSnapshot(new(-5, -2, 4, 3), source);

        var cropped = snapshot.Crop(new(-4, -1, 2, 2));

        Assert.Equal(2, cropped.Width);
        Assert.Equal(2, cropped.Height);
        Assert.Equal(Pixel(source, 1, 1), Pixel(cropped, 0, 0));
        Assert.Equal(Pixel(source, 2, 1), Pixel(cropped, 1, 0));
        Assert.Equal(Pixel(source, 1, 2), Pixel(cropped, 0, 1));
        Assert.Equal(Pixel(source, 2, 2), Pixel(cropped, 1, 1));
    }

    [Fact]
    public void Crop_supports_the_snapshot_boundary_and_cross_monitor_region()
    {
        var source = CreateImage(6, 2);
        var snapshot = new FrozenDesktopSnapshot(new(-3, 10, 6, 2), source);

        var cropped = snapshot.Crop(new(-1, 10, 4, 2));

        Assert.Equal(4, cropped.Width);
        Assert.Equal(2, cropped.Height);
        Assert.Equal(source.Bgra32[8..24].Concat(source.Bgra32[32..48]), cropped.Bgra32);
    }

    [Theory]
    [InlineData(-4, 0, 2, 1)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-3, 9, 1, 1)]
    [InlineData(-3, 10, 7, 1)]
    [InlineData(-3, 10, 1, 3)]
    public void Crop_rejects_empty_or_out_of_bounds_regions(int x, int y, int width, int height)
    {
        var snapshot = new FrozenDesktopSnapshot(new(-3, 10, 6, 2), CreateImage(6, 2));

        Assert.ThrowsAny<ArgumentException>(() => snapshot.Crop(new(x, y, width, height)));
    }

    private static CapturedImage CreateImage(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var value = checked((byte)(pixel + 1));
            bytes[pixel * 4] = value;
            bytes[pixel * 4 + 1] = (byte)(value + 40);
            bytes[pixel * 4 + 2] = (byte)(value + 80);
            bytes[pixel * 4 + 3] = 255;
        }

        return new CapturedImage(width, height, bytes);
    }

    private static byte[] Pixel(CapturedImage image, int x, int y)
    {
        var offset = (y * image.Width + x) * 4;
        return image.Bgra32[offset..(offset + 4)];
    }
}
