using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     The boundary member carries the explicit sub-boundary for both kinds of operation.
/// </summary>
/// <remarks>
///     <c>FromAction</c> and <c>FromMutation</c> both copy <c>SubBoundaryName</c>. A mutation that
///     lost it — a trait or a <c>[Resource]</c> that generates a mutation with a group of its own —
///     would land in silence on the group the namespace infers instead of on its own.
/// </remarks>
public class BoundaryMemberModelTests
{
    [Fact]
    public void FromMutation_CarriesTheSubBoundaryName()
    {
        var mutation = new MutationModel
        {
            Namespace = "TestApp.Booking",
            TypeName = "AddReservationNoteMutation",
            FullTypeName = "global::TestApp.Booking.AddReservationNoteMutation",
            Accessibility = "public",
            EntityTypeName = "ReservationNote",
            EntityFullTypeName = "global::TestApp.Booking.ReservationNote",
            Mode = MutationModeValue.Create,
            SubBoundaryName = "ReservationNotes",
        };

        BoundaryMemberModel.FromMutation(mutation).SubBoundaryName.Should().Be("ReservationNotes");
    }

    [Fact]
    public void FromAction_CarriesTheSubBoundaryName()
    {
        var action = new ActionModel
        {
            Namespace = "TestApp.Booking",
            TypeName = "AddReservationNoteAction",
            FullTypeName = "global::TestApp.Booking.AddReservationNoteAction",
            Accessibility = "public",
            IsVoid = true,
            SubBoundaryName = "ReservationNotes",
        };

        BoundaryMemberModel.FromAction(action).SubBoundaryName.Should().Be("ReservationNotes");
    }
}
