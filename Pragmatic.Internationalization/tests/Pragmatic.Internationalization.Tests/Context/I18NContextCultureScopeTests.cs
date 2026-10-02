using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Scopes;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Context;

// Test scope implementations
public class InvoicingScope : ICultureScope
{
    public static string Name => "invoicing";
    public static CultureCode DefaultCulture => CultureCode.Italian;
}

public class ReportingScope : ICultureScope
{
    public static string Name => "reporting";
    public static CultureCode DefaultCulture => CultureCode.EnglishUS;
}

[Collection("I18nContext")]
public class I18NContextCultureScopeTests
{
    public I18NContextCultureScopeTests()
    {
        I18NContext.Clear();
    }

    [Fact]
    public void GetScope_UnsetScope_ReturnsScopeDefaultCulture()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.English);

        // Act
        var culture = I18NContext.Current.GetScope<InvoicingScope>();

        // Assert - Returns InvoicingScope.DefaultCulture (Italian), not UICulture
        culture.Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void GetScope_RegisteredScope_ReturnsCulture()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.English);
        I18NContext.SetScope<InvoicingScope>(CultureCode.German);

        // Act
        var culture = I18NContext.Current.GetScope<InvoicingScope>();

        // Assert
        culture.Should().Be(CultureCode.German);
    }

    [Fact]
    public void SetScope_TypedScope_SetsCorrectly()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.English);

        // Act
        I18NContext.SetScope<ReportingScope>(CultureCode.French);

        // Assert
        I18NContext.Current.GetScope<ReportingScope>().Should().Be(CultureCode.French);
    }

    [Fact]
    public void SetScope_TypedScope_UsesCorrectName()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.English);

        // Act - Set via typed scope
        I18NContext.SetScope<InvoicingScope>(CultureCode.German);

        // Assert - Readable via string name too
        I18NContext.Current.GetScope("invoicing").Should().Be(CultureCode.German);
    }

    [Fact]
    public void WithScope_TypedScope_RestoresAfter()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.English);
        I18NContext.SetScope<InvoicingScope>(CultureCode.Italian);

        // Act
        CultureCode insideCulture = default!;
        I18NContext.WithScope<InvoicingScope>(CultureCode.German, () =>
        {
            insideCulture = I18NContext.Current.GetScope<InvoicingScope>();
        });

        // Assert
        insideCulture.Should().Be(CultureCode.German);
        I18NContext.Current.GetScope<InvoicingScope>().Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void WithScope_TypedScope_Function_ReturnsValue()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.English);

        // Act
        var result = I18NContext.WithScope<InvoicingScope, string>(CultureCode.German, () =>
        {
            return I18NContext.Current.GetScope<InvoicingScope>().Code;
        });

        // Assert
        result.Should().Be("de");
    }

    [Fact]
    public async Task WithScopeAsync_TypedScope_RestoresAfter()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.English);

        // Act
        CultureCode insideCulture = default!;
        await I18NContext.WithScopeAsync<InvoicingScope>(CultureCode.French, async () =>
        {
            await Task.Delay(1).ConfigureAwait(true);
            insideCulture = I18NContext.Current.GetScope<InvoicingScope>();
        }).ConfigureAwait(true);

        // Assert
        insideCulture.Should().Be(CultureCode.French);
    }

    [Fact]
    public void I18N_Facade_GetScope_Works()
    {
        // Arrange
        I18N.SetCulture(CultureCode.English);
        I18N.SetScope<InvoicingScope>(CultureCode.German);

        // Act
        var culture = I18N.GetScope<InvoicingScope>();

        // Assert
        culture.Should().Be(CultureCode.German);
    }

    [Fact]
    public void I18N_Facade_GetScope_UnsetScope_ReturnsDefault()
    {
        // Arrange
        I18N.SetCulture(CultureCode.English);

        // Act
        var culture = I18N.GetScope<ReportingScope>();

        // Assert
        culture.Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void LocalizedString_GetForScope_UsesTypedScope()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.English);
        I18NContext.SetScope<InvoicingScope>(CultureCode.Italian);

        var name = LocalizedString.From(
            ("en", "Widget"),
            ("it", "Componente"),
            ("de", "Gerät")
        );

        // Act
        var result = name.GetForScope<InvoicingScope>();

        // Assert
        result.Should().Be("Componente");
    }

    [Fact]
    public void LocalizedString_GetForScope_UnsetScope_UsesScopeDefault()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.English);
        // InvoicingScope not set — defaults to Italian

        var name = LocalizedString.From(
            ("en", "Widget"),
            ("it", "Componente")
        );

        // Act
        var result = name.GetForScope<InvoicingScope>();

        // Assert - Uses InvoicingScope.DefaultCulture (Italian)
        result.Should().Be("Componente");
    }

    [Fact]
    public void MultipleScopes_IndependentCultures()
    {
        // Arrange
        I18NContext.SetCulture(CultureCode.English);
        I18NContext.SetScope<InvoicingScope>(CultureCode.German);
        I18NContext.SetScope<ReportingScope>(CultureCode.French);

        // Assert
        I18NContext.Current.GetScope<InvoicingScope>().Should().Be(CultureCode.German);
        I18NContext.Current.GetScope<ReportingScope>().Should().Be(CultureCode.French);
    }
}
