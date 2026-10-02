using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Actions.Transforms;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Unit tests for <see cref="SubBoundaryTransform.InferSubBoundary" />.
/// </summary>
public class SubBoundaryTransformTests
{
    [Fact]
    public void InferSubBoundary_DirectMatch_ReturnsSub()
    {
        var result = SubBoundaryTransform.InferSubBoundary(
            "Showcase.Booking", "Showcase.Booking.Guests.Mutations");

        result.Should().Be("Guests");
    }

    [Fact]
    public void InferSubBoundary_SiblingMatch_ReturnsSub()
    {
        // Boundary in Showcase.Booking.Actions, member in Showcase.Booking.Guests.Mutations
        // Parent of boundary = Showcase.Booking, which is prefix of member
        var result = SubBoundaryTransform.InferSubBoundary(
            "Showcase.Booking.Actions", "Showcase.Booking.Guests.Mutations");

        result.Should().Be("Guests");
    }

    [Fact]
    public void InferSubBoundary_FlatNamespace_ReturnsNull()
    {
        var result = SubBoundaryTransform.InferSubBoundary(
            "Showcase.Booking", "Showcase.Booking.Mutations");

        result.Should().BeNull();
    }

    [Fact]
    public void InferSubBoundary_MultiLevel_ReturnsDottedPath()
    {
        var result = SubBoundaryTransform.InferSubBoundary(
            "Showcase.Catalog", "Showcase.Catalog.Properties.Photos.Mutations");

        result.Should().Be("Properties.Photos");
    }

    [Fact]
    public void InferSubBoundary_NoOperationType_ReturnsEntireRelative()
    {
        // When member namespace has no operation type segment, all relative segments are sub-boundary
        var result = SubBoundaryTransform.InferSubBoundary(
            "Showcase.Booking", "Showcase.Booking.Guests");

        result.Should().Be("Guests");
    }

    [Fact]
    public void InferSubBoundary_SameNamespace_ReturnsNull()
    {
        var result = SubBoundaryTransform.InferSubBoundary(
            "Showcase.Booking", "Showcase.Booking");

        result.Should().BeNull();
    }

    /// <summary>
    ///     A member outside the boundary's namespace has no segment "between the boundary and the
    ///     operation type", so it has no group. The parent match exists for a boundary class filed
    ///     under an operation-type folder (<c>Booking.Actions</c>), not for a sibling module: reading
    ///     <c>Showcase.Billing.Mutations</c> as the group <c>Billing</c> of <c>Showcase.Booking</c>
    ///     invented an API from a namespace that has nothing to do with that boundary.
    /// </summary>
    [Fact]
    public void InferSubBoundary_ASiblingNamespace_IsNotAGroup()
    {
        var result = SubBoundaryTransform.InferSubBoundary(
            "Showcase.Booking", "Showcase.Billing.Mutations");

        result.Should().BeNull();
    }

    /// <summary>
    ///     <c>Infrastructure/</c> keeps its segment in the namespace — namespace = folder, no
    ///     exceptions — and the inference discards it like an operation-type segment: an operation
    ///     filed there lands on the root, not in a group called <c>Infrastructure</c>.
    /// </summary>
    [Fact]
    public void InferSubBoundary_Infrastructure_IsNotAGroup()
    {
        SubBoundaryTransform.InferSubBoundary(
                "Showcase.Booking", "Showcase.Booking.Infrastructure.Services")
            .Should().BeNull();

        // Jobs is not a recognised segment; inference stops at Infrastructure, so this is not the
        // two-level group "Infrastructure.Jobs" — nor PRAG0412 with it.
        SubBoundaryTransform.InferSubBoundary(
                "Showcase.Booking", "Showcase.Booking.Infrastructure.Jobs")
            .Should().BeNull();
    }

    /// <summary>
    ///     <c>Enums/</c> at the module root is a kind, not a resource, and does not become a group.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The same trap the folder <c>Endpoints/</c> had at a module root, and the reason it is
    ///     worth closing: an enum folder holds no operation, so nothing shows the gap — until somebody
    ///     files one there and it lands in a group called <c>Enums</c>, silently. <c>Entities</c> and
    ///     <c>Dtos</c> were already recognised for the same reason; this is the third of the same kind.
    /// </remarks>
    [Fact]
    public void InferSubBoundary_Enums_IsNotAGroup()
    {
        SubBoundaryTransform.InferSubBoundary(
                "Showcase.Booking", "Showcase.Booking.Enums")
            .Should().BeNull();

        // And the control: a segment that is genuinely a resource still becomes one.
        SubBoundaryTransform.InferSubBoundary(
                "Showcase.Booking", "Showcase.Booking.Reservations.Mutations")
            .Should().Be("Reservations");
    }

    [Fact]
    public void InferSubBoundary_CompletelyUnrelated_ReturnsNull()
    {
        var result = SubBoundaryTransform.InferSubBoundary(
            "Showcase.Booking", "OtherApp.Billing.Mutations");

        result.Should().BeNull();
    }

    [Fact]
    public void InferSubBoundary_OperationTypeAtRoot_ReturnsNull()
    {
        // "Actions" is an operation type segment, not a sub-boundary
        var result = SubBoundaryTransform.InferSubBoundary(
            "Showcase.Booking", "Showcase.Booking.Actions");

        result.Should().BeNull();
    }

    [Fact]
    public void InferSubBoundary_SubFollowedByOperationType_ReturnsOnlySub()
    {
        var result = SubBoundaryTransform.InferSubBoundary(
            "Showcase.Booking", "Showcase.Booking.Guests.Queries");

        result.Should().Be("Guests");
    }

    [Fact]
    public void InferSubBoundary_SiblingWithSub_ReturnsCorrectSub()
    {
        // Boundary at Showcase.Booking.Actions, member at Showcase.Booking.Guests.Events
        var result = SubBoundaryTransform.InferSubBoundary(
            "Showcase.Booking.Actions", "Showcase.Booking.Guests.Events");

        result.Should().Be("Guests");
    }
}
