using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Extensions;

namespace Pragmatic.Internationalization.Tests.Extensions;

[Collection("I18nContext")]
public class NumberExtensionsTests
{
    [Fact]
    public void FormatNumber_Int_UsesCurrentCultureGrouping()
    {
        // Pin culture explicitly via the context the formatter actually reads (EffectiveCulture),
        // so the assertion does not depend on the machine's ambient CultureInfo (runner is it-IT).
        I18NContext.Clear();
        try
        {
            I18NContext.SetCulture("en-US");

            var result = 1234.FormatNumber();

            // Int overload uses "N0" → grouping only, no fractional digits.
            result.Should().Be("1,234");
        }
        finally
        {
            I18NContext.Clear();
        }
    }

    [Fact]
    public void FormatNumber_Long_UsesCurrentCultureGrouping()
    {
        I18NContext.Clear();
        try
        {
            I18NContext.SetCulture("en-US");

            var result = 1000000L.FormatNumber();

            // Long overload uses "N0" → grouping only, no fractional digits.
            result.Should().Be("1,000,000");
        }
        finally
        {
            I18NContext.Clear();
        }
    }

    [Fact]
    public void FormatNumber_Decimal_HonorsCultureSeparators()
    {
        I18NContext.Clear();
        try
        {
            I18NContext.SetCulture("de-DE");

            var result = 1234.5m.FormatNumber();

            // de-DE uses '.' for grouping and ',' for decimals.
            result.Should().Be("1.234,50");
        }
        finally
        {
            I18NContext.Clear();
        }
    }

    [Fact]
    public void FormatPercent_Decimal_AppliesPercentFormat()
    {
        I18NContext.Clear();
        try
        {
            I18NContext.SetCulture("en-US");

            var result = 0.25m.FormatPercent();

            result.Should().Contain("25").And.Contain("%");
        }
        finally
        {
            I18NContext.Clear();
        }
    }

    [Fact]
    public void FormatNumber_SpecifiedDecimals_RoundsToPlaces()
    {
        I18NContext.Clear();
        try
        {
            I18NContext.SetCulture("en-US");

            var result = 3.14159m.FormatNumber(2);

            result.Should().Be("3.14");
        }
        finally
        {
            I18NContext.Clear();
        }
    }

    [Fact]
    public void FormatNumber_ZeroDecimals_ProducesNoFraction()
    {
        I18NContext.Clear();
        try
        {
            I18NContext.SetCulture("en-US");

            var result = 9.99m.FormatNumber(0);

            result.Should().Be("10");
        }
        finally
        {
            I18NContext.Clear();
        }
    }

    [Fact]
    public void FormatFileSize_FormatsHumanReadable()
    {
        I18NContext.Clear();
        try
        {
            I18NContext.SetCulture("en-US");

            1024L.FormatFileSize().Should().Contain("KB");
            0L.FormatFileSize().Should().Be("0 B");
        }
        finally
        {
            I18NContext.Clear();
        }
    }
}
