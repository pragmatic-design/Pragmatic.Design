using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Testing;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Testing;

/// <summary>
///     Tests for TestI18NScope testing utility.
/// </summary>
public class TestI18NScopeTests : IDisposable
{
    public TestI18NScopeTests()
    {
        // Set a known initial state
        I18NContext.SetCulture(CultureCode.EnglishUS);
    }

    public void Dispose()
    {
        I18NContext.Clear();
    }

    #region Basic Scope

    [Fact]
    public void Constructor_WithCulture_SetsCulture()
    {
        using var scope = new TestI18NScope(CultureCode.Italian);

        I18N.Culture.Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void Dispose_RestoresPreviousCulture()
    {
        I18NContext.SetCulture(CultureCode.German);

        using (new TestI18NScope(CultureCode.Italian))
        {
            I18N.Culture.Should().Be(CultureCode.Italian);
        }

        I18N.Culture.Should().Be(CultureCode.German);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var scope = new TestI18NScope(CultureCode.Italian);
        scope.Dispose();

        var act = () => scope.Dispose();

        act.Should().NotThrow();
    }

    #endregion

    #region Separate UI and Data Cultures

    [Fact]
    public void Constructor_WithUIAndDataCultures_SetsBoth()
    {
        using var scope = new TestI18NScope(CultureCode.Italian, CultureCode.EnglishUS);

        I18N.UI.Should().Be(CultureCode.Italian);
        I18N.Data.Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void Constructor_WithUIAndDataCultures_RestoresBoth()
    {
        var config = new I18NConfig
        {
            DefaultUICulture = CultureCode.German,
            DefaultDataCulture = CultureCode.French,
            SyncScopes = false
        };
        I18NContext.SetFromConfig(config);

        using (new TestI18NScope(CultureCode.Italian, CultureCode.EnglishUS))
        {
            I18N.UI.Should().Be(CultureCode.Italian);
            I18N.Data.Should().Be(CultureCode.EnglishUS);
        }

        I18N.UI.Should().Be(CultureCode.German);
        I18N.Data.Should().Be(CultureCode.French);
    }

    #endregion

    #region Nested Scopes

    [Fact]
    public void NestedScopes_EachRestoresCorrectly()
    {
        I18NContext.SetCulture(CultureCode.English);

        using (new TestI18NScope(CultureCode.German))
        {
            I18N.Culture.Should().Be(CultureCode.German);

            using (new TestI18NScope(CultureCode.Italian))
            {
                I18N.Culture.Should().Be(CultureCode.Italian);

                using (new TestI18NScope(CultureCode.French))
                {
                    I18N.Culture.Should().Be(CultureCode.French);
                }

                I18N.Culture.Should().Be(CultureCode.Italian);
            }

            I18N.Culture.Should().Be(CultureCode.German);
        }

        I18N.Culture.Should().Be(CultureCode.English);
    }

    #endregion

    #region Currency Integration

    [Fact]
    public void Scope_SetsCultureCurrency()
    {
        using var scope = new TestI18NScope(CultureCode.Italian);

        I18N.Currency.Should().Be(CurrencyCode.EUR);
    }

    [Fact]
    public void Scope_DifferentCulturesHaveDifferentCurrencies()
    {
        using (new TestI18NScope(CultureCode.EnglishUS))
        {
            I18N.Currency.Should().Be(CurrencyCode.USD);
        }

        using (new TestI18NScope(CultureCode.EnglishUK))
        {
            I18N.Currency.Should().Be(CurrencyCode.GBP);
        }

        using (new TestI18NScope(CultureCode.GermanSwitzerland))
        {
            I18N.Currency.Should().Be(CurrencyCode.CHF);
        }
    }

    #endregion

    #region Thread Culture Sync

    [Fact]
    public void Scope_SyncsThreadCulture()
    {
        using var scope = new TestI18NScope(CultureCode.Italian);

        Thread.CurrentThread.CurrentUICulture.Name.Should().Be("it");
    }

    [Fact]
    public void Dispose_RestoresThreadCulture()
    {
        var originalUICulture = Thread.CurrentThread.CurrentUICulture;

        using (new TestI18NScope(CultureCode.Italian))
        {
            // Culture changed inside scope
            Thread.CurrentThread.CurrentUICulture.Name.Should().Be("it");
        }

        // Restored after scope
        Thread.CurrentThread.CurrentUICulture.Should().Be(originalUICulture);
    }

    #endregion
}
