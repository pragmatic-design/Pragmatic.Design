using Pragmatic.Internationalization.Types;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Internationalization.Tests.Types;

/// <summary>
///     The plural category of a whole number is CLDR's (#626), for every language whose rule disagreed with CLDR's.
/// </summary>
/// <remarks>
///     Each case is a sample CLDR lists for that category in <c>common/supplemental/plurals.xml</c> (read on the main branch,
///     2026-11-10). Only whole numbers are covered: <see cref="PluralRules.GetCategory" /> takes an <c>int</c>, so the operands for
///     visible fraction digits are always zero. A front end that formats with CLDR's rule must agree with the back end on every one of
///     these.
/// </remarks>
public class PluralRulesFollowCldrTests
{
    /// <summary>Italian, Spanish and Catalan: one is 1 alone, not 0 — "0 ticket sono ancora aperti".</summary>
    [Theory]
    [InlineData("it", 0, PluralCategory.Other)]
    [InlineData("it", 1, PluralCategory.One)]
    [InlineData("it", 2, PluralCategory.Other)]
    [InlineData("it-IT", 0, PluralCategory.Other)]
    [InlineData("es", 0, PluralCategory.Other)]
    [InlineData("es", 1, PluralCategory.One)]
    [InlineData("ca", 0, PluralCategory.Other)]
    [InlineData("ca", 1, PluralCategory.One)]
    public void ItalianSpanishCatalan_OneIsOneAlone(string culture, int count, PluralCategory expected)
        => PluralRules.GetCategory(culture, count).Should().Be(expected);

    /// <summary>French and Portuguese (Brazil, CLDR's <c>pt</c>): one is 0 and 1.</summary>
    [Theory]
    [InlineData("fr", 0, PluralCategory.One)]
    [InlineData("fr", 1, PluralCategory.One)]
    [InlineData("fr", 2, PluralCategory.Other)]
    [InlineData("pt", 0, PluralCategory.One)]
    [InlineData("pt-BR", 1, PluralCategory.One)]
    [InlineData("pt", 2, PluralCategory.Other)]
    public void FrenchPortuguese_OneIsZeroAndOne(string culture, int count, PluralCategory expected)
        => PluralRules.GetCategory(culture, count).Should().Be(expected);

    /// <summary>European Portuguese has its own rule in CLDR (<c>pt_PT</c>), Italian's.</summary>
    [Theory]
    [InlineData("pt-PT", 0, PluralCategory.Other)]
    [InlineData("pt-PT", 1, PluralCategory.One)]
    [InlineData("pt-PT", 1_000_000, PluralCategory.Many)]
    public void EuropeanPortuguese_FollowsItsOwnRule(string culture, int count, PluralCategory expected)
        => PluralRules.GetCategory(culture, count).Should().Be(expected);

    /// <summary>A whole number of millions is many in all five: "1 milione di ticket", "un million de tickets".</summary>
    [Theory]
    [InlineData("it", 1_000_000, PluralCategory.Many)]
    [InlineData("it", 2_000_000, PluralCategory.Many)]
    [InlineData("it", 1_000_001, PluralCategory.Other)]
    [InlineData("it", 100_000, PluralCategory.Other)]
    [InlineData("es", 3_000_000, PluralCategory.Many)]
    [InlineData("ca", 1_000_000, PluralCategory.Many)]
    [InlineData("fr", 1_000_000, PluralCategory.Many)]
    [InlineData("pt", 5_000_000, PluralCategory.Many)]
    public void ARoundMillion_IsMany(string culture, int count, PluralCategory expected)
        => PluralRules.GetCategory(culture, count).Should().Be(expected);

    /// <summary>Vietnamese is <c>i = 0 or n = 1</c>, not a language without plurals.</summary>
    [Theory]
    [InlineData("vi", 0, PluralCategory.One)]
    [InlineData("vi", 1, PluralCategory.One)]
    [InlineData("vi", 2, PluralCategory.Other)]
    public void Vietnamese_OneIsZeroAndOne(string culture, int count, PluralCategory expected)
        => PluralRules.GetCategory(culture, count).Should().Be(expected);

    /// <summary>Turkish, Azerbaijani, Georgian, Kyrgyz, Kazakh and Uzbek are <c>n = 1</c>: they have a one.</summary>
    [Theory]
    [InlineData("tr", 1, PluralCategory.One)]
    [InlineData("tr", 0, PluralCategory.Other)]
    [InlineData("tr", 2, PluralCategory.Other)]
    [InlineData("az", 1, PluralCategory.One)]
    [InlineData("ka", 1, PluralCategory.One)]
    [InlineData("ky", 1, PluralCategory.One)]
    [InlineData("kk", 1, PluralCategory.One)]
    [InlineData("uz", 1, PluralCategory.One)]
    public void TurkicAndGeorgian_HaveAOne(string culture, int count, PluralCategory expected)
        => PluralRules.GetCategory(culture, count).Should().Be(expected);

    /// <summary>Malayalam, Marathi, Tamil and Telugu are <c>n = 1</c>; Hindi, Bengali, Gujarati and Kannada are <c>i = 0 or n = 1</c>.</summary>
    [Theory]
    [InlineData("ml", 0, PluralCategory.Other)]
    [InlineData("mr", 0, PluralCategory.Other)]
    [InlineData("ta", 0, PluralCategory.Other)]
    [InlineData("te", 0, PluralCategory.Other)]
    [InlineData("ta", 1, PluralCategory.One)]
    [InlineData("hi", 0, PluralCategory.One)]
    [InlineData("bn", 0, PluralCategory.One)]
    [InlineData("hi", 2, PluralCategory.Other)]
    [InlineData("pa", 0, PluralCategory.One)]
    public void IndicLanguages_SplitOnZero(string culture, int count, PluralCategory expected)
        => PluralRules.GetCategory(culture, count).Should().Be(expected);

    /// <summary>Romanian few is <c>n = 0 or n != 1 and n % 100 = 1..19</c>: 101 and 1001 are few.</summary>
    [Theory]
    [InlineData("ro", 1, PluralCategory.One)]
    [InlineData("ro", 0, PluralCategory.Few)]
    [InlineData("ro", 2, PluralCategory.Few)]
    [InlineData("ro", 19, PluralCategory.Few)]
    [InlineData("ro", 101, PluralCategory.Few)]
    [InlineData("ro", 1001, PluralCategory.Few)]
    [InlineData("ro", 20, PluralCategory.Other)]
    [InlineData("ro", 100, PluralCategory.Other)]
    public void Romanian_FewIncludesWhatEndsInOneToNineteen(string culture, int count, PluralCategory expected)
        => PluralRules.GetCategory(culture, count).Should().Be(expected);

    /// <summary>Latvian zero is <c>n % 10 = 0 or n % 100 = 11..19</c>: 10, 11–19 and 20 are zero, not other.</summary>
    [Theory]
    [InlineData("lv", 0, PluralCategory.Zero)]
    [InlineData("lv", 10, PluralCategory.Zero)]
    [InlineData("lv", 11, PluralCategory.Zero)]
    [InlineData("lv", 19, PluralCategory.Zero)]
    [InlineData("lv", 20, PluralCategory.Zero)]
    [InlineData("lv", 100, PluralCategory.Zero)]
    [InlineData("lv", 1, PluralCategory.One)]
    [InlineData("lv", 21, PluralCategory.One)]
    [InlineData("lv", 2, PluralCategory.Other)]
    [InlineData("lv", 22, PluralCategory.Other)]
    public void Latvian_ZeroIsWhatEndsInZeroOrElevenToNineteen(string culture, int count, PluralCategory expected)
        => PluralRules.GetCategory(culture, count).Should().Be(expected);

    /// <summary>The message a caller reads: the Italian zero takes the plural form, the case the issue reported.</summary>
    [Fact]
    public void AnItalianZero_ReadsThePlural()
    {
        var message = PluralString.FromDictionary(new Dictionary<string, string>
        {
            ["one"] = "{count} ticket è ancora aperto",
            ["other"] = "{count} ticket sono ancora aperti"
        });

        message.Format(0, "tickets.open", "it").Value.Should().Be("0 ticket sono ancora aperti");
    }

    /// <summary>A message with no many form reads other for a million: the new category falls back, it does not empty the text.</summary>
    [Fact]
    public void AMillion_WithNoManyForm_ReadsOther()
    {
        var message = PluralString.FromDictionary(new Dictionary<string, string>
        {
            ["one"] = "{count} ticket",
            ["other"] = "{count} tickets"
        });

        message.Format(1_000_000, "tickets", "it").Value.Should().Be("1000000 tickets");
    }
}
