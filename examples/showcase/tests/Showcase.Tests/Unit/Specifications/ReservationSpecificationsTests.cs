using Pragmatic.Composition.Attributes;
using Pragmatic.Testing.Assertions;
using Showcase.Booking.Entities;
using Showcase.Booking.Enums;
using Xunit;

namespace Showcase.Tests.Unit.Specifications;

/// <summary>
/// Tests composable specification pattern for reservations.
/// Demonstrates: Spec composition with And/Or, AndIf for dynamic filters.
/// </summary>
/// <remarks>
/// The rules live in the other half of the entity's generated <c>ReservationSpecifications</c>, beside <c>ById</c>,
/// which is where the documentation tells an application to put them.
/// </remarks>
public class ReservationSpecificationsTests
{
    /// <summary>
    /// A promoted rule's derived query is emitted in the namespace of the class that declares the rule: with the
    /// rules on the entity's partial, that is the entities' namespace.
    /// </summary>
    [Fact]
    public void IsConfirmed_IsPromotedToAQuery_InTheEntitiesNamespace()
        => typeof(IsConfirmedQuery).Namespace.Should().Be("Showcase.Booking.Entities");

    private static readonly Guid PropertyA = Guid.NewGuid();
    private static readonly Guid PropertyB = Guid.NewGuid();
    private static readonly Guid RoomTypeA = Guid.NewGuid();
    private static readonly Guid GuestA = Guid.NewGuid();

    [Fact]
    public void IsActive_ExcludesCancelled()
    {
        var spec = ReservationSpecifications.IsActive();
        var active = CreateReservation();
        active.SetStatus(ReservationStatus.Confirmed);
        var cancelled = CreateReservation();
        cancelled.SetStatus(ReservationStatus.Cancelled);

        spec.IsSatisfiedBy(active).Should().BeTrue();
        spec.IsSatisfiedBy(cancelled).Should().BeFalse();
    }

    [Fact]
    public void IsActive_ExcludesSoftDeleted()
    {
        var spec = ReservationSpecifications.IsActive();
        var deleted = CreateReservation();
        deleted.SetStatus(ReservationStatus.Confirmed);
        deleted.IsDeleted = true;

        spec.IsSatisfiedBy(deleted).Should().BeFalse();
    }

    [Fact]
    public void ForGuest_MatchesCorrectGuest()
    {
        var spec = ReservationSpecifications.ForGuest(GuestA);
        var match = CreateReservation(guestId: GuestA);
        var noMatch = CreateReservation();

        spec.IsSatisfiedBy(match).Should().BeTrue();
        spec.IsSatisfiedBy(noMatch).Should().BeFalse();
    }

    [Fact]
    public void Overlapping_DetectsOverlap()
    {
        var checkIn = DateTimeOffset.UtcNow.AddDays(1);
        var checkOut = DateTimeOffset.UtcNow.AddDays(5);
        var spec = ReservationSpecifications.Overlapping(PropertyA, checkIn, checkOut);

        // Overlapping: reservation spans days 2-4 (within 1-5)
        var overlapping = CreateReservation(propertyId: PropertyA);
        overlapping.SetCheckIn(checkIn.AddDays(1));
        overlapping.SetCheckOut(checkIn.AddDays(3));

        // Non-overlapping: reservation is after checkout
        var nonOverlapping = CreateReservation(propertyId: PropertyA);
        nonOverlapping.SetCheckIn(checkOut.AddDays(1));
        nonOverlapping.SetCheckOut(checkOut.AddDays(3));

        spec.IsSatisfiedBy(overlapping).Should().BeTrue();
        spec.IsSatisfiedBy(nonOverlapping).Should().BeFalse();
    }

    [Fact]
    public void Overlapping_WithRoomType_FiltersCorrectly()
    {
        var checkIn = DateTimeOffset.UtcNow.AddDays(1);
        var checkOut = DateTimeOffset.UtcNow.AddDays(5);
        var spec = ReservationSpecifications.Overlapping(PropertyA, RoomTypeA, checkIn, checkOut);

        var matchingRoomType = CreateReservation(propertyId: PropertyA, roomTypeId: RoomTypeA);
        matchingRoomType.SetCheckIn(checkIn.AddDays(1));
        matchingRoomType.SetCheckOut(checkIn.AddDays(3));

        var wrongRoomType = CreateReservation(propertyId: PropertyA, roomTypeId: Guid.NewGuid());
        wrongRoomType.SetCheckIn(checkIn.AddDays(1));
        wrongRoomType.SetCheckOut(checkIn.AddDays(3));

        spec.IsSatisfiedBy(matchingRoomType).Should().BeTrue();
        spec.IsSatisfiedBy(wrongRoomType).Should().BeFalse();
    }

    [Fact]
    public void Search_DynamicComposition_WithAllFilters()
    {
        var spec = ReservationSpecifications.Search(
            guestId: GuestA,
            propertyId: PropertyA,
            status: ReservationStatus.Confirmed);

        var match = CreateReservation(guestId: GuestA, propertyId: PropertyA);
        match.SetStatus(ReservationStatus.Confirmed);

        var wrongGuest = CreateReservation(propertyId: PropertyA);
        wrongGuest.SetStatus(ReservationStatus.Confirmed);

        spec.IsSatisfiedBy(match).Should().BeTrue();
        spec.IsSatisfiedBy(wrongGuest).Should().BeFalse();
    }

    [Fact]
    public void Search_WithNoFilters_ReturnsActive()
    {
        var spec = ReservationSpecifications.Search();

        var active = CreateReservation();
        var cancelled = CreateReservation();
        cancelled.SetStatus(ReservationStatus.Cancelled);

        spec.IsSatisfiedBy(active).Should().BeTrue();
        spec.IsSatisfiedBy(cancelled).Should().BeFalse();
    }

    private static Reservation CreateReservation(
        Guid? guestId = null,
        Guid? propertyId = null,
        Guid? roomTypeId = null)
    {
        return Reservation.Create(
            guestId: guestId ?? Guid.NewGuid(),
            propertyId: propertyId ?? PropertyA,
            roomTypeId: roomTypeId ?? RoomTypeA,
            checkIn: DateTimeOffset.UtcNow.AddDays(1),
            checkOut: DateTimeOffset.UtcNow.AddDays(3),
            numberOfGuests: 1,
            totalAmount: 300m,
            currency: "EUR");
    }
}
