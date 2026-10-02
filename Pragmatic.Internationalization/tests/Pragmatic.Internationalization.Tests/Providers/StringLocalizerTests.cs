using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Providers;

[Collection("I18nContext")]
public class StringLocalizerTests
{
    private readonly I18NOptions _options = new()
    {
        DefaultUICulture = CultureCode.English
    };
    private readonly InMemoryLocalizationProvider _provider = new InMemoryLocalizationProvider()
        .AddString("en", "greeting", "Hello")
        .AddString("en", "greeting.name", "Hello, {0}!")
        .AddString("en", "greeting.full", "Hello, {0} {1}!")
        .AddString("it", "greeting", "Ciao")
        .AddString("it", "greeting.name", "Ciao, {0}!")
        .AddPlural("en", "items",
            (PluralCategory.One, "{count} item"),
            (PluralCategory.Other, "{count} items"))
        .AddPlural("it", "items",
            (PluralCategory.One, "{count} elemento"),
            (PluralCategory.Other, "{count} elementi"));

    [Fact]
    public void Indexer_Key_ReturnsLocalizedString()
    {
        // Arrange
        var localizer = new StringLocalizer(_provider, _options, "en");

        // Act
        var result = localizer["greeting"];

        // Assert
        result.Value.Should().Be("Hello");
        result.Key.Should().Be("greeting");
        result.IsLocalized.Should().BeTrue();
    }

    [Fact]
    public void Indexer_KeyNotFound_ReturnsMissing()
    {
        // Arrange
        var localizer = new StringLocalizer(_provider, _options, "en");

        // Act
        var result = localizer["unknown.key"];

        // Assert
        result.IsMissing.Should().BeTrue();
        result.Value.Should().Be("unknown.key");
    }

    [Fact]
    public void Indexer_WithArgs_InterpolatesPositionalArgs()
    {
        // Arrange
        var localizer = new StringLocalizer(_provider, _options, "en");

        // Act
        var result = localizer["greeting.name", "World"];

        // Assert
        result.Value.Should().Be("Hello, World!");
    }

    [Fact]
    public void Indexer_WithMultipleArgs_InterpolatesAll()
    {
        // Arrange
        var localizer = new StringLocalizer(_provider, _options, "en");

        // Act
        var result = localizer["greeting.full", "John", "Doe"];

        // Assert
        result.Value.Should().Be("Hello, John Doe!");
    }

    [Fact]
    public void Plural_One_ReturnsCorrectForm()
    {
        // Arrange
        var localizer = new StringLocalizer(_provider, _options, "en");

        // Act
        var result = localizer.Plural("items", 1);

        // Assert
        result.Value.Should().Be("1 item");
    }

    [Fact]
    public void Plural_Multiple_ReturnsCorrectForm()
    {
        // Arrange
        var localizer = new StringLocalizer(_provider, _options, "en");

        // Act
        var result = localizer.Plural("items", 5);

        // Assert
        result.Value.Should().Be("5 items");
    }

    [Fact]
    public void Plural_Italian_ReturnsCorrectForm()
    {
        // Arrange
        var localizer = new StringLocalizer(_provider, _options, "it");

        // Act
        var one = localizer.Plural("items", 1);
        var many = localizer.Plural("items", 5);

        // Assert
        one.Value.Should().Be("1 elemento");
        many.Value.Should().Be("5 elementi");
    }

    [Fact]
    public void Plural_NotFound_ReturnsMissing()
    {
        // Arrange
        var localizer = new StringLocalizer(_provider, _options, "en");

        // Act
        var result = localizer.Plural("unknown.plural", 5);

        // Assert
        result.IsMissing.Should().BeTrue();
    }

    [Fact]
    public void Culture_ReturnsConfiguredCulture()
    {
        // Arrange
        var localizer = new StringLocalizer(_provider, _options, "it");

        // Assert
        localizer.Culture.Should().Be("it");
    }

    [Fact]
    public void WithCulture_CreatesDifferentCultureLocalizer()
    {
        // Arrange
        var localizer = new StringLocalizer(_provider, _options, "en");

        // Act
        var italianLocalizer = localizer.WithCulture("it");

        // Assert
        italianLocalizer.Culture.Should().Be("it");
        italianLocalizer["greeting"].Value.Should().Be("Ciao");
    }

    [Fact]
    public void Fallback_WhenKeyMissingInCulture_UsesFallback()
    {
        // Arrange
        _provider.AddString("en", "only.in.english", "English only");
        var localizer = new StringLocalizer(_provider, _options, "de");

        // Act
        var result = localizer["only.in.english"];

        // Assert - Falls back to "en"
        result.Value.Should().Be("English only");
        result.IsLocalized.Should().BeTrue();
    }

    [Fact]
    public void ParentFallback_WhenSpecificCultureMissing_UsesParent()
    {
        // Arrange
        _provider.AddString("it", "parent.key", "Valore italiano");
        var options = new I18NOptions
        {
            DefaultUICulture = CultureCode.English
            // Default fallback chain: it-IT -> it -> en
        };
        var localizer = new StringLocalizer(_provider, options, "it-IT");

        // Act
        var result = localizer["parent.key"];

        // Assert - "it-IT" falls back to "it"
        result.Value.Should().Be("Valore italiano");
    }

    [Fact]
    public void Plural_FallsBackSimpleString_WhenPluralNotFound()
    {
        // Arrange
        _provider.AddString("en", "simple.count", "{count} things");
        var localizer = new StringLocalizer(_provider, _options, "en");

        // Act
        var result = localizer.Plural("simple.count", 5);

        // Assert - Uses simple string with {count} replaced
        result.Value.Should().Be("5 things");
    }

    [Fact]
    public void Plural_FallsBackSimpleString_AcrossFallbackChain()
    {
        // The simple-string fallback branch must honor the same fallback chain as GetString(),
        // not just the requested culture.
        // Arrange — key exists only as a simple string in "en"; current culture is "de"
        // whose default fallback chain (de -> en) should resolve it.
        _provider.AddString("en", "simple.count.fallback", "{count} things");
        var localizer = new StringLocalizer(_provider, _options, "de");

        // Act
        var result = localizer.Plural("simple.count.fallback", 5);

        // Assert — trying only the "de" culture would return Missing
        result.IsMissing.Should().BeFalse();
        result.Value.Should().Be("5 things");
    }

    [Fact]
    public void Plural_WithAdditionalArgs_InterpolatesBoth()
    {
        // Arrange
        _provider.AddPlural("en", "items.in",
            (PluralCategory.One, "{count} item in {0}"),
            (PluralCategory.Other, "{count} items in {0}"));
        var localizer = new StringLocalizer(_provider, _options, "en");

        // Act
        var result = localizer.Plural("items.in", 3, "the cart");

        // Assert
        result.Value.Should().Be("3 items in the cart");
    }
}
