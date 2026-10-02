using System.Globalization;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Internationalization.Types;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Templating.I18N.Tests;

/// <summary>
///     <c>{{ line.net | currency }}</c> over a <see cref="Money" />: the amount carries its own currency,
///     and the reader's culture decides how it is written.
/// </summary>
/// <remarks>
///     The pipe took a bare number and a currency code written into the template, so an invoice in the
///     customer's currency could not say which one — and the workaround, formatting the amount before it
///     reached the template, formatted it outside the reader's language.
/// </remarks>
public sealed class ACurrencyPipeOverMoneyTests
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    [Fact]
    public void AnAmount_IsWrittenInItsOwnCurrency_TheReadersWay()
    {
        var result = new CurrencyPipe().Execute(Money.From(1234.5m, CurrencyCode.USD), [], Italian);

        result.Should().Be(1234.5m.ToString("C", NumberFormatWithSymbol("$")));
    }

    /// <summary>A code written in the template still wins: the template is the author's last word.</summary>
    [Fact]
    public void ACodeInTheTemplate_StillWins()
    {
        var result = new CurrencyPipe().Execute(Money.From(10m, CurrencyCode.USD), ["EUR"], Italian);

        result!.ToString().Should().Contain("€");
    }

    private static NumberFormatInfo NumberFormatWithSymbol(string symbol)
    {
        var format = (NumberFormatInfo)Italian.NumberFormat.Clone();
        format.CurrencySymbol = symbol;
        return format;
    }
}
