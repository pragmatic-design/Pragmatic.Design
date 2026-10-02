using Pragmatic.Testing.Assertions;
using Pragmatic.Actions.Attributes;
using Showcase.Billing.Actions;
using Xunit;

namespace Showcase.Tests.Unit.Lifecycle;

/// <summary>
/// Tests verifying the [CompositeAction] attribute usage on CreateInvoiceWithFeesAction.
/// </summary>
public class CompositeActionTests
{
    /// <summary>
    ///     It must <b>not</b> carry <c>[CompositeAction]</c>.
    /// </summary>
    /// <remarks>
    ///     The action declares no step properties, so the attribute would generate nothing and the work
    ///     is done by its own Execute. Pinning the attribute's presence would pin a decoration: a test
    ///     that passes while the thing it names does not happen. PRAG0427 reports the shape; this
    ///     asserts the action does not claim it.
    /// </remarks>
    [Fact]
    public void CreateInvoiceWithFeesAction_DoesNotClaimToBeAComposite()
    {
        typeof(CreateInvoiceWithFeesAction)
            .GetCustomAttributes(typeof(CompositeActionAttribute), false)
            .Should().BeEmpty();
    }

    [Fact]
    public void CreateInvoiceWithFeesAction_IsInternalAction()
    {
        var attr = typeof(CreateInvoiceWithFeesAction)
            .GetCustomAttributes(typeof(DomainActionAttribute), false);

        attr.Should().ContainSingle();
        var domainAction = (DomainActionAttribute)attr[0];
        domainAction.Internal.Should().BeTrue();
    }

    [Fact]
    public void ServiceFeeRequest_IsImmutableRecord()
    {
        // Init properties rather than a positional record: the type carries
        // validation rules now, so its members are declared one per line with the rule on each.
        var request = new ServiceFeeRequest { ServiceName = "Minibar", Amount = 15.50m };

        request.ServiceName.Should().Be("Minibar");
        request.Amount.Should().Be(15.50m);
        request.ServiceDate.Should().BeNull();
    }

    [Fact]
    public void ServiceFeeRequest_WithOptionalDate()
    {
        var date = new DateTimeOffset(2026, 3, 9, 10, 0, 0, TimeSpan.Zero);
        var request = new ServiceFeeRequest
        {
            ServiceName = "Room Service",
            Amount = 45m,
            ServiceDate = date
        };

        request.ServiceDate.Should().Be(date);
    }
}
