using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace SnappySnap.Localization;

public sealed record LanguageOption(string Code, string DisplayName);

// Shared text resources contain no UI types; presentation and application messages use the same locale.
public sealed class L : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new("SnappySnap.Core.Localization.Strings", typeof(L).Assembly);
    private static readonly Dictionary<string, string> CultureNames = new(StringComparer.Ordinal)
    {
        ["en"] = "en-US",
        ["ru"] = "ru-RU",
        ["zh-CN"] = "zh-CN",
        ["ja-JP"] = "ja-JP",
        ["es-ES"] = "es-ES"
    };

    public const string DefaultLanguage = "en";
    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("en", "English"),
        new("ru", "Русский"),
        new("zh-CN", "简体中文"),
        new("ja-JP", "日本語"),
        new("es-ES", "Español")
    ];

    public static L Current { get; } = new();
    public string Language { get; private set; } = DefaultLanguage;
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(CultureNames[Current.Language]);
    public event PropertyChangedEventHandler? PropertyChanged;

    public static bool IsSupported(string? language) => language is not null && CultureNames.ContainsKey(language);

    public static void SetLanguage(string language)
    {
        if (!IsSupported(language)) throw new ArgumentOutOfRangeException(nameof(language));
        Current.Language = language;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = Culture;
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs(nameof(Language)));
    }

    // Literal file names, numbers and format identifiers can also pass through shared UI helpers.
    public static string T(string text) => Resources.GetString(text, Culture) ?? Resources.GetString(text, CultureInfo.GetCultureInfo("en-US")) ?? text;
    public static string F(string format, params object?[] args) => string.Format(Culture, T(format), args);
}
