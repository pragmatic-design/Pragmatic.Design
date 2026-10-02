using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Types;
using Showcase.Billing.Dtos;
using Xunit;

namespace Showcase.Tests.Unit.ValueObjects;

/// <summary>
/// Tests Money value type usage in DTOs.
/// Demonstrates: Money from I18n module — currency safety, formatting.
/// </summary>
public class MoneyDtoTests
{
    [Fact]
    public void Money_PreventsMixingCurrencies()
    {
        var eur = Money.From(100m, CurrencyCode.FromCode("EUR"));
        var usd = Money.From(100m, CurrencyCode.FromCode("USD"));

        eur.Should().NotBe(usd);
    }

    [Fact]
    public void Money_Zero_HasCorrectCurrency()
    {
        var zero = Money.Zero(CurrencyCode.FromCode("EUR"));

        zero.IsZero.Should().BeTrue();
        zero.Currency.Code.Should().Be("EUR");
    }

    [Fact]
    public void InvoiceMoneyDto_FormatsTotal()
    {
        var dto = new InvoiceMoneyDto
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = "INV-001",
            Total = Money.From(1234.56m, CurrencyCode.FromCode("EUR")),
            Tax = Money.From(234.56m, CurrencyCode.FromCode("EUR")),
            Status = "Paid"
        };

        dto.TotalFormatted.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CurrencyCode_ParsesValidCode()
    {
        var eur = CurrencyCode.FromCode("EUR");

        eur.Code.Should().Be("EUR");
        eur.Name.Should().NotBeNullOrEmpty();
        eur.MinorUnits.Should().Be(2);
    }

    [Fact]
    public void CurrencyCode_InvalidCode_Throws()
    {
        var act = () => CurrencyCode.FromCode("INVALID");

        act.Should().Throw<Exception>();
    }
}
