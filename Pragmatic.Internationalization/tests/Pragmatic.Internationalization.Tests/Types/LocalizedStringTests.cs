using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Types;

[Collection("I18nContext")]
public class LocalizedStringTests
{
    public LocalizedStringTests()
    {
        // Reset context for each test
        I18NContext.Clear();
    }

    [Fact]
    public void Empty_ReturnsEmptyLocalizedString()
    {
        // Act
        var ls = LocalizedString.Empty;

        // Assert
        ls.IsEmpty.Should().BeTrue();
        ls.Count.Should().Be(0);
    }

    [Fact]
    public void From_SingleValue_CreatesWithDefaultCulture()
    {
        // Act
        var ls = LocalizedString.From("Hello");

        // Assert
        ls.Count.Should().Be(1);
        ls["en"].Should().Be("Hello");
        ls.HasDefaultCulture.Should().BeTrue();
    }

    [Fact]
    public void From_Tuples_CreatesMultipleTranslations()
    {
        // Act
        var ls = LocalizedString.From(
            ("en", "Hello"),
            ("it", "Ciao"),
            ("de", "Hallo")
        );

        // Assert
        ls.Count.Should().Be(3);
        ls["en"].Should().Be("Hello");
        ls["it"].Should().Be("Ciao");
        ls["de"].Should().Be("Hallo");
    }

    [Fact]
    public void From_Dictionary_CreatesLocalizedString()
    {
        // Arrange
        var dict = new Dictionary<string, string>
        {
            ["en"] = "Good morning",
            ["es"] = "Buenos días"
        };

        // Act
        var ls = LocalizedString.From(dict);

        // Assert
        ls["en"].Should().Be("Good morning");
        ls["es"].Should().Be("Buenos días");
    }

    [Fact]
    public void Set_AddsOrUpdatesTranslation()
    {
        // Arrange
        var ls = new LocalizedString();

        // Act
        ls.Set("en", "Hello")
            .Set("it", "Ciao");

        // Assert
        ls["en"].Should().Be("Hello");
        ls["it"].Should().Be("Ciao");
    }

    [Fact]
    public void SetCurrent_UsesCurrentCulture()
    {
        // Arrange
        I18NContext.SetCulture("fr-FR");
        var ls = new LocalizedString();

        // Act
        ls.SetCurrent("Bonjour");

        // Assert
        ls["fr-FR"].Should().Be("Bonjour");
    }

    [Fact]
    public void Get_WithFallback_ReturnsFallbackValue()
    {
        // Arrange
        var ls = LocalizedString.From(
            ("en", "Hello"),
            ("it", "Ciao")
        );

        // Act - Request culture not present, should fallback to "en"
        var value = ls.Get("de");

        // Assert - Falls back to default "en"
        value.Should().Be("Hello");
    }

    [Fact]
    public void Get_WithParentFallback_ReturnsParentValue()
    {
        // Arrange
        var ls = LocalizedString.From(
            ("en", "Hello"),
            ("it", "Ciao")
        );

        // Act - "it-IT" should fall back to "it"
        var value = ls.Get("it-IT");

        // Assert
        value.Should().Be("Ciao");
    }

    [Fact]
    public void GetExact_NoFallback_ReturnsNull()
    {
        // Arrange
        var ls = LocalizedString.From(("en", "Hello"));

        // Act
        var value = ls.GetExact("it");

        // Assert
        value.Should().BeNull();
    }

    [Fact]
    public void TryGetExact_ReturnsTrueWhenExists()
    {
        // Arrange
        var ls = LocalizedString.From(("en", "Hello"));

        // Act & Assert
        ls.TryGetExact("en", out var value).Should().BeTrue();
        value.Should().Be("Hello");

        ls.TryGetExact("it", out _).Should().BeFalse();
    }

    [Fact]
    public void Value_UsesCurrentCulture()
    {
        // Arrange
        I18NContext.SetCulture("it");
        var ls = LocalizedString.From(
            ("en", "Hello"),
            ("it", "Ciao")
        );

        // Act
        var value = ls.Value;

        // Assert
        value.Should().Be("Ciao");
    }

    [Fact]
    public void ImplicitConversion_ToStringUsesCurrentCulture()
    {
        // Arrange
        I18NContext.SetCulture("de");
        var ls = LocalizedString.From(
            ("en", "Hello"),
            ("de", "Hallo")
        );

        // Act
        string value = ls;

        // Assert
        value.Should().Be("Hallo");
    }

    [Fact]
    public void Remove_RemovesTranslation()
    {
        // Arrange
        var ls = LocalizedString.From(
            ("en", "Hello"),
            ("it", "Ciao")
        );

        // Act
        var removed = ls.Remove("it");

        // Assert
        removed.Should().BeTrue();
        ls.HasCulture("it").Should().BeFalse();
        ls.Count.Should().Be(1);
    }

    [Fact]
    public void Clear_RemovesAllTranslations()
    {
        // Arrange
        var ls = LocalizedString.From(
            ("en", "Hello"),
            ("it", "Ciao")
        );

        // Act
        ls.Clear();

        // Assert
        ls.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Contains_SearchesInCurrentCulture()
    {
        // Arrange
        I18NContext.SetCulture("en");
        var ls = LocalizedString.From(("en", "Hello World"));

        // Act & Assert
        ls.Contains("World").Should().BeTrue();
        ls.Contains("world").Should().BeTrue(); // Case insensitive
        ls.Contains("xyz").Should().BeFalse();
    }

    [Fact]
    public void HasCulture_ChecksExactCulture()
    {
        // Arrange
        var ls = LocalizedString.From(("en", "Hello"));

        // Assert
        ls.HasCulture("en").Should().BeTrue();
        ls.HasCulture("EN").Should().BeTrue(); // Case insensitive
        ls.HasCulture("it").Should().BeFalse();
    }

    [Fact]
    public void Cultures_ReturnsAllCultureCodes()
    {
        // Arrange
        var ls = LocalizedString.From(
            ("en", "Hello"),
            ("it", "Ciao"),
            ("de", "Hallo")
        );

        // Assert
        ls.Cultures.Should().BeEquivalentTo("en", "it", "de");
    }

    [Fact]
    public void Equality_SameTranslations_AreEqual()
    {
        // Arrange
        var a = LocalizedString.From(("en", "Hello"), ("it", "Ciao"));
        var b = LocalizedString.From(("en", "Hello"), ("it", "Ciao"));

        // Assert
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentTranslations_NotEqual()
    {
        // Arrange
        var a = LocalizedString.From(("en", "Hello"));
        var b = LocalizedString.From(("en", "Hi"));

        // Assert
        (a == b).Should().BeFalse();
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void IReadOnlyDictionary_Implementation()
    {
        // Arrange
        var ls = LocalizedString.From(("en", "Hello"), ("it", "Ciao"));
        IReadOnlyDictionary<string, string> dict = ls;

        // Assert
        dict.Count.Should().Be(2);
        dict.Keys.Should().BeEquivalentTo("en", "it");
        dict.Values.Should().BeEquivalentTo("Hello", "Ciao");
        dict.ContainsKey("en").Should().BeTrue();
        dict.TryGetValue("it", out var val).Should().BeTrue();
        val.Should().Be("Ciao");
    }

    [Fact]
    public void ToString_ReturnsCurrentCultureValue()
    {
        // Arrange
        I18NContext.SetCulture("it");
        var ls = LocalizedString.From(("en", "Hello"), ("it", "Ciao"));

        // Act
        var str = ls.ToString();

        // Assert
        str.Should().Be("Ciao");
    }
}
