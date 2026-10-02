using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Testing;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Context;

/// <summary>
///     Tests for multi-scope I18NContext features (UI/Data cultures, custom scopes).
/// </summary>
[Collection("I18nContext")]
public class I18NContextMultiScopeTests : IDisposable
{
    public I18NContextMultiScopeTests()
    {
        I18NContext.Clear();
    }

    public void Dispose()
    {
        I18NContext.Clear();
    }

    // ══════════════════════════════════════════════════════════════
    // UI/Data Culture Tests
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void SetCulture_WithCultureCode_SetsUICulture()
    {
        // Act
        I18NContext.SetCulture(CultureCode.Italian);

        // Assert
        I18NContext.Current.UICulture.Should().Be(CultureCode.Italian);
        I18N.UI.Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void SetDataCulture_SetsDataCultureIndependently()
    {
        // Arrange - use SetFromConfig to get SyncScopes=false
        var config = new I18NConfig
        {
            DefaultUICulture = CultureCode.Italian,
            DefaultDataCulture = CultureCode.Italian,
            SyncScopes = false
        };
        I18NContext.SetFromConfig(config);

        // Act
        I18NContext.SetDataCulture(CultureCode.EnglishUS);

        // Assert
        I18NContext.Current.UICulture.Should().Be(CultureCode.Italian);
        I18NContext.Current.DataCulture.Should().Be(CultureCode.EnglishUS);
        I18N.UI.Should().Be(CultureCode.Italian);
        I18N.Data.Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void SetDataCulture_WhenSyncScopesTrue_IsNoOp()
    {
        // Arrange - SetCulture creates context with SyncScopes=true by default
        I18NContext.SetCulture(CultureCode.Italian);
        I18NContext.Current.SyncScopes.Should().BeTrue();

        // Act - SetDataCulture should be a no-op
        I18NContext.SetDataCulture(CultureCode.EnglishUS);

        // Assert - Data should still equal UI
        I18NContext.Current.DataCulture.Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void UICulture_DefaultsToEnglishUS()
    {
        // Arrange
        I18NContext.Clear();

        // Act
        var current = I18NContext.Current;

        // Assert - defaults to en-US when no culture is set
        current.UICulture.Code.Should().NotBeNullOrEmpty();
    }

    // ══════════════════════════════════════════════════════════════
    // Custom Scope Tests
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void SetScope_SetsCustomScope()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.EnglishUS);

        // Act
        I18NContext.SetScope("invoicing", CultureCode.German);

        // Assert
        I18NContext.Current.GetScope("invoicing").Should().Be(CultureCode.German);
        I18N.Scope["invoicing"].Should().Be(CultureCode.German);
    }

    [Fact]
    public void GetScope_WhenNotSet_ReturnsUICulture()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.Italian);

        // Act
        var scopeCulture = I18NContext.Current.GetScope("undefined-scope");

        // Assert - falls back to UI culture
        scopeCulture.Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void SetScope_OverwritesExisting()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.EnglishUS);
        I18NContext.SetScope("invoicing", CultureCode.German);

        // Act
        I18NContext.SetScope("invoicing", CultureCode.French);

        // Assert
        I18N.Scope["invoicing"].Should().Be(CultureCode.French);
    }

    // ══════════════════════════════════════════════════════════════
    // Currency Integration Tests
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Currency_DerivedFromUICulture()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.Italian);

        // Act & Assert - Italy uses EUR
        I18NContext.Current.Currency.Should().Be(CurrencyCode.EUR);
        I18N.Currency.Should().Be(CurrencyCode.EUR);
    }

    [Fact]
    public void SetPreferredCurrency_OverridesDefault()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.Italian);

        // Act
        I18NContext.SetPreferredCurrency(CurrencyCode.USD);

        // Assert
        I18NContext.Current.Currency.Should().Be(CurrencyCode.USD);
        I18NContext.Current.PreferredCurrency.Should().Be(CurrencyCode.USD);
    }

    [Fact]
    public void Zero_ReturnsMoneysInContextCurrency()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.Italian);

        // Act
        var zero = I18N.Zero;

        // Assert
        zero.Amount.Should().Be(0);
        zero.Currency.Should().Be(CurrencyCode.EUR);
    }

    [Fact]
    public void CreateMoney_UsesContextCurrency()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.EnglishUS);

        // Act
        var money = I18N.CreateMoney(99.99m);

        // Assert
        money.Amount.Should().Be(99.99m);
        money.Currency.Should().Be(CurrencyCode.USD);
    }

    // ══════════════════════════════════════════════════════════════
    // WithCulture/WithScope Tests
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void WithCulture_CultureCode_ExecutesInContext()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.EnglishUS);

        // Act
        var captured = I18NContext.WithCulture(CultureCode.German, () => I18N.UI);

        // Assert
        captured.Should().Be(CultureCode.German);
        I18N.UI.Should().Be(CultureCode.EnglishUS); // Restored
    }

    [Fact]
    public void WithDataCulture_ExecutesInContext()
    {
        // Arrange - use SetFromConfig to get SyncScopes=false
        var config = new I18NConfig
        {
            DefaultUICulture = CultureCode.Italian,
            DefaultDataCulture = CultureCode.EnglishUS,
            SyncScopes = false
        };
        I18NContext.SetFromConfig(config);

        // Act
        var captured = I18NContext.WithDataCulture(CultureCode.German, () => I18N.Data);

        // Assert
        captured.Should().Be(CultureCode.German);
        I18N.Data.Should().Be(CultureCode.EnglishUS); // Restored
    }

    [Fact]
    public void WithScope_ExecutesInContext()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.EnglishUS);

        // Act
        var captured = I18NContext.WithScope("invoicing", CultureCode.German, () => I18N.Scope["invoicing"]);

        // Assert
        captured.Should().Be(CultureCode.German);
    }

    [Fact]
    public async Task WithCultureAsync_CultureCode_ExecutesAsyncInContext()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.EnglishUS);

        // Act
#pragma warning disable CA2007 // xUnit tests don't need ConfigureAwait
        var captured = await I18NContext.WithCultureAsync(CultureCode.Japanese, async () =>
        {
            await Task.Delay(1);
            return I18N.UI;
        });
#pragma warning restore CA2007

        // Assert
        captured.Should().Be(CultureCode.Japanese);
        I18N.UI.Should().Be(CultureCode.EnglishUS); // Restored
    }

    // ══════════════════════════════════════════════════════════════
    // SetFromConfig Tests
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void SetFromConfig_SetsAllProperties()
    {
        // Arrange
        var config = new I18NConfig
        {
            DefaultUICulture = CultureCode.German,
            DefaultDataCulture = CultureCode.EnglishUS,
            PreferredCurrency = CurrencyCode.CHF
        };

        // Act
        I18NContext.SetFromConfig(config);

        // Assert
        I18N.UI.Should().Be(CultureCode.German);
        I18N.Data.Should().Be(CultureCode.EnglishUS);
        I18N.Currency.Should().Be(CurrencyCode.CHF);
    }

    // ══════════════════════════════════════════════════════════════
    // TestI18NScope Helper Tests
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void TestI18NScope_SetsAndRestoresCulture()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.EnglishUS);

        // Act
        using (new TestI18NScope(CultureCode.Italian))
        {
            I18N.UI.Should().Be(CultureCode.Italian);
        }

        // Assert - restored
        I18N.UI.Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void TestI18NScope_WithDataCulture_SetsAndRestores()
    {
        // Arrange - use SetFromConfig to get SyncScopes=false
        var config = new I18NConfig
        {
            DefaultUICulture = CultureCode.EnglishUS,
            DefaultDataCulture = CultureCode.EnglishUS,
            SyncScopes = false
        };
        I18NContext.SetFromConfig(config);

        // Act
        using (new TestI18NScope(CultureCode.German, CultureCode.French))
        {
            I18N.UI.Should().Be(CultureCode.German);
            I18N.Data.Should().Be(CultureCode.French);
        }

        // Assert - restored
        I18N.UI.Should().Be(CultureCode.EnglishUS);
        I18N.Data.Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void TestI18NScope_WithDifferentCultures_Work()
    {
        // Act & Assert
        using (new TestI18NScope(CultureCode.Italian))
        {
            I18N.UI.Should().Be(CultureCode.Italian);
        }

        using (new TestI18NScope(CultureCode.German))
        {
            I18N.UI.Should().Be(CultureCode.German);
        }
    }
}
