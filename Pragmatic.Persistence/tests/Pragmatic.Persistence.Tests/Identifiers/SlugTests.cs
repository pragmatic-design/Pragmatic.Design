using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Identifiers;

namespace Pragmatic.Persistence.Tests.Identifiers;

public class SlugTests
{
    [Fact]
    public void Create_SimpleText_ReturnsSlug()
    {
        var result = Slug.Create("Hello World");

        result.Should().Be("hello-world");
    }

    [Fact]
    public void Create_WithAccents_NormalizesUnicode()
    {
        var result = Slug.Create("Café Résumé");

        result.Should().Be("cafe-resume");
    }

    [Fact]
    public void Create_WithSpecialChars_Removes()
    {
        var result = Slug.Create("C# 101");

        result.Should().Be("c-101");
    }

    [Fact]
    public void Create_WithMultipleSpaces_CollapsesToHyphen()
    {
        var result = Slug.Create("Multiple   Spaces   Here");

        result.Should().Be("multiple-spaces-here");
    }

    [Fact]
    public void Create_WithLeadingTrailingSpecial_NoHyphens()
    {
        var result = Slug.Create("  --Hello World--  ");

        result.Should().NotStartWith("-");
        result.Should().NotEndWith("-");
        result.Should().Contain("hello-world");
    }

    [Fact]
    public void Create_WithMaxLength_TruncatesAtWordBoundary()
    {
        var result = Slug.Create("this is a very long title that should be truncated", maxLength: 20);

        result.Length.Should().BeLessThanOrEqualTo(20);
        result.Should().NotEndWith("-");
    }

    [Fact]
    public void Create_WithShortMaxLength_StillWorks()
    {
        var result = Slug.Create("Hello World", maxLength: 3);

        result.Length.Should().BeLessThanOrEqualTo(3);
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Create_WithNull_Throws()
    {
        var act = () => Slug.Create(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithEmpty_Throws()
    {
        var act = () => Slug.Create("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithMaxLengthZero_Throws()
    {
        var act = () => Slug.Create("Hello", maxLength: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_WithNumbers_PreservesNumbers()
    {
        var result = Slug.Create("Article 42");

        result.Should().Be("article-42");
    }

    [Fact]
    public void CreateUnique_AddsRandomSuffix()
    {
        var result = Slug.CreateUnique("Hello World");

        // Should contain base slug + hyphen + suffix
        result.Should().StartWith("hello-world-");
    }

    [Fact]
    public void CreateUnique_CorrectSuffixLength()
    {
        var result = Slug.CreateUnique("Test", suffixLength: 8);

        // "test" (4) + "-" (1) + suffix (8) = at least 13
        var parts = result.Split('-');
        parts.Last().Should().HaveLength(8);
    }

    [Fact]
    public void CreateUnique_TwoCallsDiffer()
    {
        var result1 = Slug.CreateUnique("Same Input");
        var result2 = Slug.CreateUnique("Same Input");

        result1.Should().NotBe(result2);
    }

    [Fact]
    public void CreateUnique_WithTightMaxLength_Truncates()
    {
        var result = Slug.CreateUnique("Very Long Title Here", maxLength: 15, suffixLength: 5);

        result.Length.Should().BeLessThanOrEqualTo(15);
    }

    [Fact]
    public void CreateAnonymous_ReturnsCorrectLength()
    {
        var result = Slug.CreateAnonymous(length: 8);

        result.Should().HaveLength(8);
    }

    [Fact]
    public void CreateAnonymous_ContainsOnlyValidChars()
    {
        const string suffixAlphabet = "abcdefghijkmnpqrstuvwxyz23456789";

        var result = Slug.CreateAnonymous(length: 50);

        foreach (var c in result)
            suffixAlphabet.Should().Contain(c.ToString(),
                $"character '{c}' should be in SuffixAlphabet");
    }

    [Fact]
    public void CreateAnonymous_WithZeroLength_Throws()
    {
        var act = () => Slug.CreateAnonymous(length: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void IsValid_WithValidSlug_ReturnsTrue()
    {
        Slug.IsValid("hello-world").Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithNull_ReturnsFalse()
    {
        Slug.IsValid(null).Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithUppercase_ReturnsFalse()
    {
        Slug.IsValid("Hello").Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithConsecutiveHyphens_ReturnsFalse()
    {
        Slug.IsValid("hello--world").Should().BeFalse();
    }

    [Theory]
    [InlineData("Привет")]    // Cyrillic
    [InlineData("你好世界")]    // CJK
    [InlineData("Ελληνικά")]   // Greek
    public void Create_NonLatinScript_ProducesAsciiOnlyOutput(string input)
    {
        // char.IsLetterOrDigit is true for non-Latin letters, so without care they leak into the slug,
        // producing output that fails the class's own ASCII IsValid regex and enables homograph
        // collisions. The slug must be ASCII-only.
        var slug = Slug.Create(input);

        slug.Should().MatchRegex("^[a-z0-9-]*$");
    }

    [Fact]
    public void Create_MixedLatinAndNonLatin_KeepsOnlyAscii()
    {
        var slug = Slug.Create("Café Привет 42");

        slug.Should().MatchRegex("^[a-z0-9-]*$");
        slug.Should().Contain("cafe");
        slug.Should().Contain("42");
    }
}
