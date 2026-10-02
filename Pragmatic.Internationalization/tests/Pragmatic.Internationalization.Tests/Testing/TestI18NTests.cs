using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Testing;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Testing;

/// <summary>
///     Tests for TestI18N static helper methods.
/// </summary>
public class TestI18NTests : IDisposable
{
    public TestI18NTests()
    {
        I18NContext.SetCulture(CultureCode.EnglishUS);
    }

    public void Dispose()
    {
        I18NContext.Clear();
    }

    #region WithCulture Action

    [Fact]
    public void WithCulture_Action_SetsCultureDuringExecution()
    {
        CultureCode capturedCulture = default;

        TestI18N.WithCulture(CultureCode.Italian, () =>
        {
            capturedCulture = I18N.Culture;
        });

        capturedCulture.Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void WithCulture_Action_RestoresAfterExecution()
    {
        I18NContext.SetCulture(CultureCode.German);

        TestI18N.WithCulture(CultureCode.Italian, () => { });

        I18N.Culture.Should().Be(CultureCode.German);
    }

    [Fact]
    public void WithCulture_Action_RestoresEvenOnException()
    {
        I18NContext.SetCulture(CultureCode.German);

        try
        {
            TestI18N.WithCulture(CultureCode.Italian, () => throw new InvalidOperationException());
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        I18N.Culture.Should().Be(CultureCode.German);
    }

    #endregion

    #region WithCulture Func

    [Fact]
    public void WithCulture_Func_ReturnsFunctionResult()
    {
        var result = TestI18N.WithCulture(CultureCode.Italian, () => I18N.Currency.Code);

        result.Should().Be("EUR");
    }

    [Fact]
    public void WithCulture_Func_SetsCultureDuringExecution()
    {
        var capturedCulture = TestI18N.WithCulture(CultureCode.Italian, () => I18N.Culture);

        capturedCulture.Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void WithCulture_Func_RestoresAfterExecution()
    {
        I18NContext.SetCulture(CultureCode.German);

        TestI18N.WithCulture(CultureCode.Italian, () => "result");

        I18N.Culture.Should().Be(CultureCode.German);
    }

    #endregion

    #region WithCultureAsync

    [Fact]
    public async Task WithCultureAsync_Action_SetsCultureDuringExecution()
    {
        CultureCode capturedCulture = default;

        await TestI18N.WithCultureAsync(CultureCode.Italian, async () =>
        {
            await Task.Yield();
            capturedCulture = I18N.Culture;
        });

        capturedCulture.Should().Be(CultureCode.Italian);
    }

    [Fact]
    public async Task WithCultureAsync_Action_RestoresAfterExecution()
    {
        I18NContext.SetCulture(CultureCode.German);

        await TestI18N.WithCultureAsync(CultureCode.Italian, async () =>
        {
            await Task.Yield();
        });

        I18N.Culture.Should().Be(CultureCode.German);
    }

    [Fact]
    public async Task WithCultureAsync_Func_ReturnsFunctionResult()
    {
        var result = await TestI18N.WithCultureAsync(CultureCode.Italian, async () =>
        {
            await Task.Yield();
            return I18N.Currency.Code;
        });

        result.Should().Be("EUR");
    }

    [Fact]
    public async Task WithCultureAsync_Func_PreservesCultureAcrossAwaits()
    {
        var cultures = new List<CultureCode>();

        await TestI18N.WithCultureAsync(CultureCode.Italian, async () =>
        {
            cultures.Add(I18N.Culture);
            await Task.Delay(10).ConfigureAwait(false);
            cultures.Add(I18N.Culture);
            await Task.Yield();
            cultures.Add(I18N.Culture);
        });

        cultures.Should().HaveCount(3);
        cultures.Should().AllSatisfy(c => c.Should().Be(CultureCode.Italian));
    }

    #endregion

    #region WithCultures (UI + Data)

    [Fact]
    public void WithCultures_Action_SetsBothCulturesDuringExecution()
    {
        CultureCode capturedUI = default;
        CultureCode capturedData = default;

        TestI18N.WithCultures(CultureCode.Italian, CultureCode.EnglishUS, () =>
        {
            capturedUI = I18N.UI;
            capturedData = I18N.Data;
        });

        capturedUI.Should().Be(CultureCode.Italian);
        capturedData.Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void WithCultures_Func_ReturnsFunctionResult()
    {
        var result = TestI18N.WithCultures(CultureCode.Italian, CultureCode.EnglishUS,
            () => $"UI={I18N.UI.Code}, Data={I18N.Data.Code}");

        result.Should().Be("UI=it, Data=en-US");
    }

    [Fact]
    public void WithCultures_RestoresBothAfterExecution()
    {
        var config = new I18NConfig
        {
            DefaultUICulture = CultureCode.German,
            DefaultDataCulture = CultureCode.French,
            SyncScopes = false
        };
        I18NContext.SetFromConfig(config);

        TestI18N.WithCultures(CultureCode.Italian, CultureCode.EnglishUS, () => { });

        I18N.UI.Should().Be(CultureCode.German);
        I18N.Data.Should().Be(CultureCode.French);
    }

    #endregion

    #region Real World Scenarios

    [Fact]
    public void MoneyFormatting_Italian_UsesComma()
    {
        var result = TestI18N.WithCulture(CultureCode.Italian, () =>
        {
            var price = Money.From(1234.56m, CurrencyCode.EUR);
            return price.Format(I18N.Culture.ToCultureInfo());
        });

        result.Should().Contain(","); // Italian uses comma as decimal separator
        result.Should().Contain("€");
    }

    [Fact]
    public void MoneyFormatting_US_UsesDot()
    {
        var result = TestI18N.WithCulture(CultureCode.EnglishUS, () =>
        {
            var price = Money.From(1234.56m, CurrencyCode.USD);
            return price.Format(I18N.Culture.ToCultureInfo());
        });

        result.Should().Contain(".");
        result.Should().Contain("$");
    }

    [Theory]
    [InlineData("it", "EUR")]
    [InlineData("en-US", "USD")]
    [InlineData("en-GB", "GBP")]
    [InlineData("de-CH", "CHF")]
    [InlineData("ja", "JPY")]
    public void Culture_HasExpectedCurrency(string cultureCode, string expectedCurrency)
    {
        var culture = CultureCode.FromString(cultureCode);

        var result = TestI18N.WithCulture(culture, () => I18N.Currency.Code);

        result.Should().Be(expectedCurrency);
    }

    #endregion
}
