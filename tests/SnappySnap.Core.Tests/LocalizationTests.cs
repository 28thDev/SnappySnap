using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using SnappySnap.Core;
using SnappySnap.Localization;
using Xunit;

namespace SnappySnap.Core.Tests;

public sealed class LocalizationTests
{
    private static readonly string[] SupportedLanguageCodes = ["en", "ru", "zh-CN", "ja-JP", "es-ES"];

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("zh-CN")]
    [InlineData("ja-JP")]
    [InlineData("es-ES")]
    public void RecordingPillStatesHaveLocalizedMessages(string language)
    {
        var resources = new ResourceManager("SnappySnap.Core.Localization.Strings", typeof(L).Assembly);
        foreach (var state in Enum.GetValues<SnappySnap.Core.RecordingState>())
            Assert.False(string.IsNullOrWhiteSpace(resources.GetString(state.ToString(), CultureInfo.GetCultureInfo(language))), state.ToString());
    }

    [Fact]
    public void Default_language_is_English_and_supported_languages_are_non_RTL()
    {
        Assert.Equal("en", L.DefaultLanguage);
        Assert.Equal("en", AppSettings.Defaults().General.Language);
        Assert.Equal(SupportedLanguageCodes, L.SupportedLanguages.Select(language => language.Code));
    }

    [Theory]
    [InlineData("zh-CN", "设置")]
    [InlineData("ja-JP", "設定")]
    [InlineData("es-ES", "Configuración")]
    public void New_catalogs_translate_the_primary_settings_label(string language, string expected)
    {
        var resources = new ResourceManager("SnappySnap.Core.Localization.Strings", typeof(L).Assembly);
        Assert.Equal(expected, resources.GetString("Settings", CultureInfo.GetCultureInfo(language)));
    }

    [Theory]
    [InlineData("en-US", "0.5 MB", "<0.1 MB")]
    [InlineData("ru-RU", "0,5 MB", "<0,1 MB")]
    public void ExplicitFileSizeLocaleDoesNotDependOnAsyncThreadCulture(string language, string expected, string small)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        Assert.Equal(expected, SnappySnap.Core.FileSizeFormatter.Format(500_000, culture));
        Assert.Equal(small, SnappySnap.Core.FileSizeFormatter.Format(1, culture));
    }

    [Fact]
    public void RussianCatalogCoversEveryEnglishMessageAndPreservesFormatArguments()
    {
        var resources = new ResourceManager("SnappySnap.Core.Localization.Strings", typeof(L).Assembly);
        var english = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var russian = resources.GetResourceSet(CultureInfo.GetCultureInfo("ru"), true, false)!;
        Assert.True(english.Cast<DictionaryEntry>().Count() > 250);
        foreach (DictionaryEntry entry in english)
        {
            var translation = russian.GetString((string)entry.Key);
            Assert.False(string.IsNullOrWhiteSpace(translation), (string)entry.Key);
            Assert.Equal(CompositeFormat.Parse((string)entry.Value!).MinimumArgumentCount,
                CompositeFormat.Parse(translation!).MinimumArgumentCount);
        }
    }
}
