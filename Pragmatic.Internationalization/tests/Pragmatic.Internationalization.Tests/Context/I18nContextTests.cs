using System.Globalization;
using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Context;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Context;

[Collection("I18nContext")]
public class I18NContextTests : IDisposable
{
    public I18NContextTests()
    {
        I18NContext.Clear();
    }

    public void Dispose()
    {
        I18NContext.Clear();
    }

    [Fact]
    public void Current_WhenNotSet_ReturnsDefaultFromCurrentCulture()
    {
        // Arrange
        I18NContext.Clear();

        // Act
        var context = I18NContext.Current;

        // Assert
        context.Should().NotBeNull();

        // On Linux CI, CurrentCulture is InvariantCulture ("") which maps to "en" via CultureCode.FromCultureInfo.
        // We verify the roundtrip is consistent rather than exact CultureInfo equality.
        var expectedCulture = string.IsNullOrEmpty(CultureInfo.CurrentCulture.Name)
            ? CultureInfo.GetCultureInfo("en")
            : CultureInfo.CurrentCulture;
        context.Culture.Should().Be(expectedCulture);
    }

    [Fact]
    public void SetCulture_WithString_SetsCurrentContext()
    {
        // Act
        I18NContext.SetCulture("it-IT");
        var context = I18NContext.Current;

        // Assert
        context.CultureCode.Should().Be("it-IT");
        context.Culture.Name.Should().Be("it-IT");
    }

    [Fact]
    public void SetCulture_WithCultureInfo_SetsCurrentContext()
    {
        // Arrange
        var culture = CultureInfo.GetCultureInfo("de-DE");

        // Act
        I18NContext.SetCulture(culture);
        var context = I18NContext.Current;

        // Assert
        context.Culture.Should().Be(culture);
    }

    [Fact]
    public void Clear_RemovesCurrentContext()
    {
        // Arrange
        I18NContext.SetCulture("fr-FR");

        // Act
        I18NContext.Clear();
        var context = I18NContext.Current;

        // Assert - Should fallback to thread culture (InvariantCulture maps to "en")
        var expectedCulture = string.IsNullOrEmpty(CultureInfo.CurrentCulture.Name)
            ? CultureInfo.GetCultureInfo("en")
            : CultureInfo.CurrentCulture;
        context.Culture.Should().Be(expectedCulture);
    }

    [Fact]
    public void ForCulture_CreatesNewContext()
    {
        // Act
        var context = I18NContext.ForCulture("es-ES");

        // Assert
        context.CultureCode.Should().Be("es-ES");
        context.Culture.Name.Should().Be("es-ES");
    }

    [Fact]
    public void WithCulture_ExecutesActionInContext()
    {
        // Arrange
        I18NContext.SetCulture("en-US");
        string? capturedCulture = null;

        // Act
        I18NContext.WithCulture("ja-JP", () => { capturedCulture = I18NContext.Current.CultureCode; });

        // Assert
        capturedCulture.Should().Be("ja-JP");
        I18NContext.Current.CultureCode.Should().Be("en-US"); // Restored
    }

    [Fact]
    public void WithCulture_ExecutesFuncInContext()
    {
        // Arrange
        I18NContext.SetCulture("en-US");

        // Act
        var result = I18NContext.WithCulture("pt-BR", () => I18NContext.Current.CultureCode);

        // Assert
        result.Should().Be("pt-BR");
        I18NContext.Current.CultureCode.Should().Be("en-US"); // Restored
    }

    [Fact]
    public async Task WithCultureAsync_ExecutesAsyncActionInContext()
    {
        // Arrange
        I18NContext.SetCulture("en-US");
        string? capturedCulture = null;

        // Act
        await I18NContext.WithCultureAsync("ko-KR", async () =>
        {
            await Task.Delay(1).ConfigureAwait(false);
            capturedCulture = I18NContext.Current.CultureCode;
        });

        // Assert
        capturedCulture.Should().Be("ko-KR");
        I18NContext.Current.CultureCode.Should().Be("en-US"); // Restored
    }

    [Fact]
    public void NormalizeCulture_HandlesVariousCases()
    {
        // Act & Assert
        I18NContext.ForCulture("EN-us").CultureCode.Should().Be("en-US");
        I18NContext.ForCulture("IT").CultureCode.Should().Be("it");
        I18NContext.ForCulture("zh-cn").CultureCode.Should().Be("zh-CN");
    }
}

public class I18NStaticShortcutTests
{
    [Fact]
    public void I18n_ProvidesStaticAccessToContext()
    {
        // Arrange
        I18NContext.SetCulture("fr-FR");

        // Act & Assert
        I18N.CultureCode.Should().Be("fr-FR");
        I18N.CultureInfo.Name.Should().Be("fr-FR");
        I18N.Current.Should().NotBeNull();
    }

    [Fact]
    public void I18n_SetCulture_Works()
    {
        // Act
        I18N.SetCulture("nl-NL");

        // Assert
        I18N.CultureCode.Should().Be("nl-NL");
    }
}
