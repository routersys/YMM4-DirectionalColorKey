using System.Collections;
using System.Globalization;
using System.Reflection;

namespace DirectionalColorKey.Tests;

public sealed class TextsTests
{
    public static readonly TheoryData<string> Cultures = ["ja-JP", "en-US", "zh-CN", "zh-TW", "ko-KR", "es-ES", "ar-SA", "id-ID"];

    static readonly string[] Keys = typeof(Texts)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(property => property.PropertyType == typeof(string) && property.Name != nameof(Texts.CurrentCulture))
        .Select(property => property.Name)
        .ToArray();

    [Fact]
    public void EveryLocalizedKeyHasAProperty()
    {
        var resources = Texts.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var keys = resources.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).Where(key => key != nameof(Texts.CurrentCulture));

        Assert.Equal(keys.Order(), Keys.Order());
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryKeyIsTranslated(string culture)
    {
        var resources = Texts.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, true)!;

        Assert.All(Keys, key => Assert.False(string.IsNullOrWhiteSpace(resources.GetString(key)), key));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryCultureCarriesItsOwnTranslations(string culture)
    {
        var cultureInfo = CultureInfo.GetCultureInfo(culture);

        Assert.Equal(culture.ToLowerInvariant(), Texts.ResourceManager.GetString(nameof(Texts.CurrentCulture), cultureInfo));
    }

    [Fact]
    public void JapaneseIsTheNeutralLanguage()
    {
        var neutral = Texts.ResourceManager.GetString(nameof(Texts.DirectionalColorKeyEffectName), CultureInfo.InvariantCulture);

        Assert.Equal("方向色分離キー", neutral);
        Assert.Equal(neutral, Texts.ResourceManager.GetString(nameof(Texts.DirectionalColorKeyEffectName), CultureInfo.GetCultureInfo("ja-JP")));
    }
}
