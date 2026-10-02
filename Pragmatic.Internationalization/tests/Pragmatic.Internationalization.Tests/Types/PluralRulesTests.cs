using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Types;

public class PluralRulesTests
{
    // English (Germanic)
    [Theory]
    [InlineData("en", 0, PluralCategory.Other)]
    [InlineData("en", 1, PluralCategory.One)]
    [InlineData("en", 2, PluralCategory.Other)]
    [InlineData("en", 5, PluralCategory.Other)]
    [InlineData("en-US", 1, PluralCategory.One)]
    [InlineData("en-GB", 10, PluralCategory.Other)]
    public void English_ReturnsCorrectCategory(string culture, int count, PluralCategory expected)
    {
        PluralRules.GetCategory(culture, count).Should().Be(expected);
    }

    // German (Germanic)
    [Theory]
    [InlineData("de", 1, PluralCategory.One)]
    [InlineData("de", 2, PluralCategory.Other)]
    [InlineData("de-DE", 1, PluralCategory.One)]
    public void German_ReturnsCorrectCategory(string culture, int count, PluralCategory expected)
    {
        PluralRules.GetCategory(culture, count).Should().Be(expected);
    }

    // French/Italian (Romance - 0 and 1 are singular)
    [Theory]
    [InlineData("fr", 0, PluralCategory.One)]
    [InlineData("fr", 1, PluralCategory.One)]
    [InlineData("fr", 2, PluralCategory.Other)]
    [InlineData("it", 0, PluralCategory.One)]
    [InlineData("it", 1, PluralCategory.One)]
    [InlineData("it-IT", 5, PluralCategory.Other)]
    public void Romance_ReturnsCorrectCategory(string culture, int count, PluralCategory expected)
    {
        PluralRules.GetCategory(culture, count).Should().Be(expected);
    }

    // Russian (East Slavic)
    [Theory]
    [InlineData("ru", 1, PluralCategory.One)] // 1 элемент
    [InlineData("ru", 2, PluralCategory.Few)] // 2 элемента
    [InlineData("ru", 5, PluralCategory.Many)] // 5 элементов
    [InlineData("ru", 11, PluralCategory.Many)] // 11 элементов
    [InlineData("ru", 21, PluralCategory.One)] // 21 элемент
    [InlineData("ru", 22, PluralCategory.Few)] // 22 элемента
    [InlineData("ru", 25, PluralCategory.Many)] // 25 элементов
    public void Russian_ReturnsCorrectCategory(string culture, int count, PluralCategory expected)
    {
        PluralRules.GetCategory(culture, count).Should().Be(expected);
    }

    // Polish
    [Theory]
    [InlineData("pl", 1, PluralCategory.One)] // 1 element
    [InlineData("pl", 2, PluralCategory.Few)] // 2 elementy
    [InlineData("pl", 5, PluralCategory.Many)] // 5 elementów
    [InlineData("pl", 12, PluralCategory.Many)] // 12 elementów
    [InlineData("pl", 22, PluralCategory.Few)] // 22 elementy
    public void Polish_ReturnsCorrectCategory(string culture, int count, PluralCategory expected)
    {
        PluralRules.GetCategory(culture, count).Should().Be(expected);
    }

    // Arabic
    [Theory]
    [InlineData("ar", 0, PluralCategory.Zero)]
    [InlineData("ar", 1, PluralCategory.One)]
    [InlineData("ar", 2, PluralCategory.Two)]
    [InlineData("ar", 5, PluralCategory.Few)]
    [InlineData("ar", 15, PluralCategory.Many)]
    [InlineData("ar", 100, PluralCategory.Other)]
    public void Arabic_ReturnsCorrectCategory(string culture, int count, PluralCategory expected)
    {
        PluralRules.GetCategory(culture, count).Should().Be(expected);
    }

    // Japanese/Chinese (no plurals)
    [Theory]
    [InlineData("ja", 0, PluralCategory.Other)]
    [InlineData("ja", 1, PluralCategory.Other)]
    [InlineData("ja", 100, PluralCategory.Other)]
    [InlineData("zh", 1, PluralCategory.Other)]
    [InlineData("zh-CN", 5, PluralCategory.Other)]
    [InlineData("ko", 10, PluralCategory.Other)]
    public void EastAsian_AlwaysReturnsOther(string culture, int count, PluralCategory expected)
    {
        PluralRules.GetCategory(culture, count).Should().Be(expected);
    }

    // Czech/Slovak
    [Theory]
    [InlineData("cs", 1, PluralCategory.One)]
    [InlineData("cs", 2, PluralCategory.Few)]
    [InlineData("cs", 5, PluralCategory.Other)]
    [InlineData("sk", 1, PluralCategory.One)]
    [InlineData("sk", 3, PluralCategory.Few)]
    public void CzechSlovak_ReturnsCorrectCategory(string culture, int count, PluralCategory expected)
    {
        PluralRules.GetCategory(culture, count).Should().Be(expected);
    }

    // Slovenian (has dual)
    [Theory]
    [InlineData("sl", 1, PluralCategory.One)]
    [InlineData("sl", 2, PluralCategory.Two)]
    [InlineData("sl", 3, PluralCategory.Few)]
    [InlineData("sl", 5, PluralCategory.Other)]
    public void Slovenian_ReturnsCorrectCategory(string culture, int count, PluralCategory expected)
    {
        PluralRules.GetCategory(culture, count).Should().Be(expected);
    }

    // Welsh (complex)
    [Theory]
    [InlineData("cy", 0, PluralCategory.Zero)]
    [InlineData("cy", 1, PluralCategory.One)]
    [InlineData("cy", 2, PluralCategory.Two)]
    [InlineData("cy", 3, PluralCategory.Few)]
    [InlineData("cy", 6, PluralCategory.Many)]
    [InlineData("cy", 10, PluralCategory.Other)]
    public void Welsh_ReturnsCorrectCategory(string culture, int count, PluralCategory expected)
    {
        PluralRules.GetCategory(culture, count).Should().Be(expected);
    }

    // Unknown culture falls back to Germanic rules
    [Fact]
    public void UnknownCulture_UsesGermanicRules()
    {
        // Unknown culture should use default Germanic (one/other)
        PluralRules.GetCategory("xx", 1).Should().Be(PluralCategory.One);
        PluralRules.GetCategory("xx", 2).Should().Be(PluralCategory.Other);
    }

    // Culture normalization
    [Fact]
    public void CultureCode_ExtractsLanguageFromFullCode()
    {
        // "en-US" should use "en" rules
        PluralRules.GetCategory("en-US", 1).Should().Be(PluralCategory.One);
        PluralRules.GetCategory("en-GB", 2).Should().Be(PluralCategory.Other);

        // "ru-RU" should use "ru" rules
        PluralRules.GetCategory("ru-RU", 5).Should().Be(PluralCategory.Many);
    }
}