using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Types;
using Showcase.Billing.Actions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Validation;

/// <summary>
///     <c>[ValidateElements]</c>: an action's validator checks the action's properties,
///     and a list is one property.
/// </summary>
/// <remarks>
///     <para>
///         <c>CreateInvoiceWithFeesAction</c> takes a list of service fees and sums their amounts
///         into the invoice's subtotal. Whatever was inside that list had never been looked at: a
///         fee with no name and a negative amount was accepted, and it <em>lowered</em> the
///         subtotal — a discount nobody granted, through a field meant to add a charge.
///     </para>
///     <para>
///         ⚠️ The rules live on <c>ServiceFeeRequest</c>, which is where they belong and where they
///         did nothing on their own: an element type's <c>Validate()</c> is only called if the list
///         says so. The declaration is what connects the two, and the error path carries the index.
///     </para>
/// </remarks>
public class WhatIsInsideTheListIsCheckedTooTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AFeeWithANegativeAmount_IsRefused_NamingTheElement()
    {
        var refused = await CreateWithAsync(new ServiceFeeRequest
        {
            ServiceName = "Spa",
            Amount = -50m
        });

        refused.IsFailure.Should().BeTrue(
            "a fee of minus fifty would have been summed into the subtotal");
        refused.Error.Code.Should().Be(ValidationError.ErrorCode,
            "it is refused by the validator and not by the body — the body never ran");

        var paths = ((ValidationError)refused.Error).Issues.Select(i => i.PropertyPath).ToList();

        paths.Should().Contain(path => path!.StartsWith("ServiceFees[0]", StringComparison.Ordinal),
            "the path carries the index, so a caller sending twenty fees is told which one — "
            + $"got [{string.Join(", ", paths)}]");
    }

    [Fact]
    public async Task AFeeWithNoName_IsRefused()
    {
        var refused = await CreateWithAsync(new ServiceFeeRequest
        {
            ServiceName = "",
            Amount = 25m
        });

        refused.IsFailure.Should().BeTrue("[Required] on the element's name is now reachable");
    }

    /// <summary>
    ///     The control: a list whose elements are all valid still goes through.
    /// </summary>
    /// <remarks>
    ///     Without it, "a bad fee is refused" is satisfied by an action that refuses every list —
    ///     which is what a validator walking the elements wrongly, or an element type whose
    ///     generated <c>Validate()</c> refuses everything, would look like.
    /// </remarks>
    [Fact]
    public async Task AFeeThatIsFine_IsAccepted()
    {
        var created = await CreateWithAsync(new ServiceFeeRequest
        {
            ServiceName = "Airport transfer",
            Amount = 45m
        });

        created.IsFailure.Should().BeFalse(
            "{0}", created.IsFailure ? created.Error.Code : "");
        created.Value.Should().NotBe(Guid.Empty, "the action answers the id of the invoice it wrote");
    }

    private async Task<Pragmatic.Result.Result<Guid, Pragmatic.Result.IError>> CreateWithAsync(
        ServiceFeeRequest fee)
    {
        using var scope = Services.CreateScope();

        var callContext = scope.ServiceProvider.GetService<ActionCallContext>();
        using var internalCall = callContext?.EnterInternalCall();

        var invoker = scope.ServiceProvider
            .GetRequiredService<IDomainActionInvoker<CreateInvoiceWithFeesAction, Guid>>();

        return await invoker.InvokeAsync(new CreateInvoiceWithFeesAction
        {
            ReservationId = Guid.CreateVersion7(),
            GuestId = Guid.CreateVersion7(),
            RoomCharge = 120m,
            Currency = "EUR",
            ServiceFees = [fee]
        });
    }
}
