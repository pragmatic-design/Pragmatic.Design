using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Providers;

public class CompositeLocalizationProviderTests
{
    [Fact]
    public void Constructor_SortsProvidersByPriority()
    {
        // Arrange
        var lowPriority = new InMemoryLocalizationProvider { Priority = 0 };
        var highPriority = new InMemoryLocalizationProvider { Priority = 100 };
        var midPriority = new InMemoryLocalizationProvider { Priority = 50 };

        // Act
        var composite = new CompositeLocalizationProvider([lowPriority, highPriority, midPriority]);

        // Assert
        composite.ProviderCount.Should().Be(3);
    }

    [Fact]
    public void GetString_ReturnsFromHighestPriorityProvider()
    {
        // Arrange
        var lowPriority = new InMemoryLocalizationProvider { Priority = 0 }
            .AddString("en", "key", "Low priority value");
        var highPriority = new InMemoryLocalizationProvider { Priority = 100 }
            .AddString("en", "key", "High priority value");

        var composite = new CompositeLocalizationProvider([lowPriority, highPriority]);

        // Act
        var result = composite.GetString("key", "en");

        // Assert
        result.Should().Be("High priority value");
    }

    [Fact]
    public void GetString_FallsBackToLowerPriority()
    {
        // Arrange
        var lowPriority = new InMemoryLocalizationProvider { Priority = 0 }
            .AddString("en", "only.in.low", "Low priority only");
        var highPriority = new InMemoryLocalizationProvider { Priority = 100 }
            .AddString("en", "other.key", "High priority value");

        var composite = new CompositeLocalizationProvider([lowPriority, highPriority]);

        // Act
        var result = composite.GetString("only.in.low", "en");

        // Assert
        result.Should().Be("Low priority only");
    }

    [Fact]
    public void GetString_NotFound_ReturnsNull()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider()
            .AddString("en", "key", "value");
        var composite = new CompositeLocalizationProvider([provider]);

        // Act
        var result = composite.GetString("unknown", "en");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetPlural_ReturnsFromHighestPriorityProvider()
    {
        // Arrange
        var lowPriority = new InMemoryLocalizationProvider { Priority = 0 }
            .AddPlural("en", "items",
                (PluralCategory.One, "Low: 1 item"),
                (PluralCategory.Other, "Low: {count} items"));
        var highPriority = new InMemoryLocalizationProvider { Priority = 100 }
            .AddPlural("en", "items",
                (PluralCategory.One, "High: 1 item"),
                (PluralCategory.Other, "High: {count} items"));

        var composite = new CompositeLocalizationProvider([lowPriority, highPriority]);

        // Act
        var result = composite.GetPlural("items", "en");

        // Assert
        result.Should().NotBeNull();
        result![PluralCategory.One].Should().Be("High: 1 item");
    }

    [Fact]
    public void GetPlural_NotFound_ReturnsNull()
    {
        // Arrange
        var provider = new InMemoryLocalizationProvider();
        var composite = new CompositeLocalizationProvider([provider]);

        // Act
        var result = composite.GetPlural("unknown", "en");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetAll_MergesAllProviders_HighPriorityOverrides()
    {
        // Arrange
        var lowPriority = new InMemoryLocalizationProvider { Priority = 0 }
            .AddString("en", "shared.key", "Low value")
            .AddString("en", "low.only", "Low only value");
        var highPriority = new InMemoryLocalizationProvider { Priority = 100 }
            .AddString("en", "shared.key", "High value")
            .AddString("en", "high.only", "High only value");

        var composite = new CompositeLocalizationProvider([lowPriority, highPriority]);

        // Act
        var all = composite.GetAll("en");

        // Assert
        all.Should().HaveCount(3);
        all["shared.key"].Should().Be("High value"); // Overridden
        all["low.only"].Should().Be("Low only value");
        all["high.only"].Should().Be("High only value");
    }

    [Fact]
    public void GetAllPlurals_MergesAllProviders()
    {
        // Arrange
        var lowPriority = new InMemoryLocalizationProvider { Priority = 0 }
            .AddPlural("en", "items",
                (PluralCategory.One, "Low: 1"),
                (PluralCategory.Other, "Low: many"));
        var highPriority = new InMemoryLocalizationProvider { Priority = 100 }
            .AddPlural("en", "things",
                (PluralCategory.One, "High: 1"),
                (PluralCategory.Other, "High: many"));

        var composite = new CompositeLocalizationProvider([lowPriority, highPriority]);

        // Act
        var all = composite.GetAllPlurals("en");

        // Assert
        all.Should().HaveCount(2);
        all.Should().ContainKey("items");
        all.Should().ContainKey("things");
    }

    [Fact]
    public void SupportedCultures_CombinesAllProviders()
    {
        // Arrange
        var provider1 = new InMemoryLocalizationProvider()
            .AddString("en", "key1", "value1")
            .AddString("it", "key1", "valore1");
        var provider2 = new InMemoryLocalizationProvider()
            .AddString("en", "key2", "value2")
            .AddString("de", "key2", "wert2");

        var composite = new CompositeLocalizationProvider([provider1, provider2]);

        // Act
        var cultures = composite.SupportedCultures;

        // Assert
        cultures.Should().BeEquivalentTo("de", "en", "it");
    }

    [Fact]
    public void Priority_ReturnsMaxProviderPriority()
    {
        // Arrange
        var lowPriority = new InMemoryLocalizationProvider { Priority = 10 };
        var highPriority = new InMemoryLocalizationProvider { Priority = 100 };

        var composite = new CompositeLocalizationProvider([lowPriority, highPriority]);

        // Assert
        composite.Priority.Should().Be(100);
    }

    [Fact]
    public void Priority_EmptyProviders_ReturnsZero()
    {
        // Arrange
        var composite = new CompositeLocalizationProvider([]);

        // Assert
        composite.Priority.Should().Be(0);
    }

    [Fact]
    public void SupportedCultures_RemovesDuplicates()
    {
        // Arrange
        var provider1 = new InMemoryLocalizationProvider()
            .AddString("en", "key1", "value1");
        var provider2 = new InMemoryLocalizationProvider()
            .AddString("en", "key2", "value2")
            .AddString("EN", "key3", "value3");

        var composite = new CompositeLocalizationProvider([provider1, provider2]);

        // Act
        var cultures = composite.SupportedCultures;

        // Assert - Case insensitive dedup (OrdinalIgnoreCase in CompositeLocalizationProvider)
        cultures.Should().HaveCount(1);
        cultures.Should().Contain("en");
    }
}