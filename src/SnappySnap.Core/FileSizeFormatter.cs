using System.Globalization;
namespace SnappySnap.Core;
public static class FileSizeFormatter
{
    public static string Format(long bytes, CultureInfo? culture = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        culture ??= CultureInfo.CurrentCulture;
        return bytes is > 0 and < 100_000 ? "<" + 0.1.ToString("0.0", culture) + " MB" : (bytes / 1_000_000d).ToString("0.0", culture) + " MB";
    }
}
