using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Providers;

public class InMemoryLocalizationProviderTests
{
    [Fact]
    public void AddString_StoresTranslation()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider();

        // Act
        provider.AddString("en", "greeting", "Hello");

        // Assert
        provider.GetString("greeting", "en").Should().Be("Hello");
    }

    [Fact]
    public void AddString_SupportsFluentChaining()
    {
        // Act
        var provider = new InMemoryLocalizationProvider()
            .AddString("en", "greeting", "Hello")
            .AddString("en", "farewell", "Goodbye")
            .AddString("it", "greeting", "Ciao");

        // Assert
        provider.GetString("greeting", "en").Should().Be("Hello");
        provider.GetString("farewell", "en").Should().Be("Goodbye");
        provider.GetString("greeting", "it").Should().Be("Ciao");
    }

    [Fact]
    public void GetString_NotFound_ReturnsNull()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider();

        // Act & Assert
        provider.GetString("unknown", "en").Should().BeNull();
    }

    [Fact]
    public void AddPlural_StoresPluralForms()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider();
        var plural = new PluralString(new Dictionary<PluralCategory, string>
        {
            [PluralCategory.One] = "{count} item",
            [PluralCategory.Other] = "{count} items"
        });

        // Act
        provider.AddPlural("en", "items", plural);

        // Assert
        var result = provider.GetPlural("items", "en");
        result.Should().NotBeNull();
        result![PluralCategory.One].Should().Be("{count} item");
        result[PluralCategory.Other].Should().Be("{count} items");
    }

    [Fact]
    public void AddPlural_WithTuples_StoresPluralForms()
    {
        // Act
        var provider = new InMemoryLocalizationProvider()
            .AddPlural("en", "items",
                (PluralCategory.One, "1 item"),
                (PluralCategory.Other, "{count} items"));

        // Assert
        var result = provider.GetPlural("items", "en");
        result.Should().NotBeNull();
        result![PluralCategory.One].Should().Be("1 item");
    }

    [Fact]
    public void GetPlural_NotFound_ReturnsNull()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider();

        // Act & Assert
        provider.GetPlural("unknown", "en").Should().BeNull();
    }

    [Fact]
    public void AddStrings_AddsMultipleTranslations()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider();
        var translations = new Dictionary<string, string>
        {
            ["greeting"] = "Hello",
            ["farewell"] = "Goodbye"
        };

        // Act
        provider.AddStrings("en", translations);

        // Assert
        provider.GetString("greeting", "en").Should().Be("Hello");
        provider.GetString("farewell", "en").Should().Be("Goodbye");
    }

    [Fact]
    public void GetAll_ReturnsAllStringsForCulture()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider()
            .AddString("en", "greeting", "Hello")
            .AddString("en", "farewell", "Goodbye")
            .AddString("it", "greeting", "Ciao");

        // Act
        var all = provider.GetAll("en");

        // Assert
        all.Should().HaveCount(2);
        all["greeting"].Should().Be("Hello");
        all["farewell"].Should().Be("Goodbye");
    }

    [Fact]
    public void GetAllPlurals_ReturnsAllPluralsForCulture()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider()
            .AddPlural("en", "items", (PluralCategory.One, "1 item"), (PluralCategory.Other, "{count} items"))
            .AddPlural("en", "messages", (PluralCategory.One, "1 message"), (PluralCategory.Other, "{count} messages"));

        // Act
        var all = provider.GetAllPlurals("en");

        // Assert
        all.Should().HaveCount(2);
        all.Should().ContainKey("items");
        all.Should().ContainKey("messages");
    }

    [Fact]
    public void SupportedCultures_ReturnsAddedCultures()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider()
            .AddString("en", "key", "value")
            .AddString("it", "key", "valore")
            .AddString("de", "key", "wert");

        // Assert
        provider.SupportedCultures.Should().BeEquivalentTo("en", "it", "de");
    }

    [Fact]
    public void RemoveString_RemovesTranslation()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider()
            .AddString("en", "greeting", "Hello");

        // Act
        var removed = provider.RemoveString("en", "greeting");

        // Assert
        removed.Should().BeTrue();
        provider.GetString("greeting", "en").Should().BeNull();
    }

    [Fact]
    public void RemoveString_NotFound_ReturnsFalse()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider();

        // Act & Assert
        provider.RemoveString("en", "unknown").Should().BeFalse();
    }

    [Fact]
    public void ClearCulture_RemovesAllForCulture()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider()
            .AddString("en", "greeting", "Hello")
            .AddString("en", "farewell", "Goodbye")
            .AddString("it", "greeting", "Ciao");

        // Act
        provider.ClearCulture("en");

        // Assert
        provider.GetString("greeting", "en").Should().BeNull();
        provider.GetString("greeting", "it").Should().Be("Ciao");
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider()
            .AddString("en", "greeting", "Hello")
            .AddString("it", "greeting", "Ciao");

        // Act
        provider.Clear();

        // Assert
        provider.SupportedCultures.Should().BeEmpty();
    }

    [Fact]
    public void Priority_CanBeSet()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider { Priority = 100 };

        // Assert
        provider.Priority.Should().Be(100);
    }
}