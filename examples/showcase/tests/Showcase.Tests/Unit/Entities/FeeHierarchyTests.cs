using Pragmatic.Testing.Assertions;
using Showcase.Billing.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests for the Fee TPH hierarchy (Fee → ServiceFee, CancellationFee).
/// Demonstrates inheritance mapping pattern.
/// </summary>
public class FeeHierarchyTests
{
    [Fact]
    public void ServiceFee_InheritsFromFee()
    {
        var fee = new ServiceFee();

        fee.Should().BeAssignableTo<Fee>();
    }

    [Fact]
    public void CancellationFee_InheritsFromFee()
    {
        var fee = new CancellationFee();

        fee.Should().BeAssignableTo<Fee>();
    }

    [Fact]
    public void ServiceFee_SetsServiceName()
    {
        var fee = new ServiceFee();
        fee.SetServiceName("Minibar");

        fee.ServiceName.Should().Be("Minibar");
    }

    [Fact]
    public void ServiceFee_SetsServiceDate()
    {
        var date = new DateTimeOffset(2026, 3, 9, 14, 0, 0, TimeSpan.Zero);
        var fee = new ServiceFee();
        fee.SetServiceDate(date);

        fee.ServiceDate.Should().Be(date);
    }

    [Fact]
    public void ServiceFee_SetsBaseProperties()
    {
        var fee = new ServiceFee();
        fee.SetAmount(25.50m);
        fee.SetCurrency("USD");
        fee.SetReason("Room service");

        fee.Amount.Should().Be(25.50m);
        fee.Currency.Should().Be("USD");
        fee.Reason.Should().Be("Room service");
    }

    [Fact]
    public void CancellationFee_SetsPenaltyRate()
    {
        var fee = new CancellationFee();
        fee.SetPenaltyRate(0.25m);

        fee.PenaltyRate.Should().Be(0.25m);
    }

    [Fact]
    public void CancellationFee_SetsOriginalAmount()
    {
        var fee = new CancellationFee();
        fee.SetOriginalAmount(500m);
        fee.SetPenaltyRate(0.25m);
        fee.SetAmount(fee.OriginalAmount * fee.PenaltyRate);

        fee.Amount.Should().Be(125m);
        fee.OriginalAmount.Should().Be(500m);
    }

    [Fact]
    public void Fee_DefaultCurrency_IsEur()
    {
        var fee = new ServiceFee();

        fee.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Fee_HasUniqueId()
    {
        var fee1 = new ServiceFee();
        var fee2 = new CancellationFee();

        fee1.PersistenceId.Should().NotBe(fee2.PersistenceId);
    }
}
