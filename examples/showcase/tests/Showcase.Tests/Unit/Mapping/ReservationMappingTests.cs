using Pragmatic.Testing.Assertions;
using Showcase.Booking.Dtos;
using Showcase.Booking.Entities;
using Showcase.Booking.Enums;
using Xunit;

namespace Showcase.Tests.Unit.Mapping;

/// <summary>
/// Tests source-generated mapping for ReservationSummaryDto.
/// Demonstrates: [MapProperty] for navigation flattening (Property.Name → PropertyName).
/// </summary>
public class ReservationMappingTests
{
    [Fact]
    public void ReservationSummaryDto_FromEntity_MapsScalarFields()
    {
        var reservation = CreateReservation();

        var dto = ReservationSummaryDto.FromEntity(reservation);

        dto.Id.Should().Be(reservation.Id);
        dto.GuestId.Should().Be(reservation.GuestId);
        dto.PropertyId.Should().Be(reservation.PropertyId);
        dto.CheckIn.Should().Be(reservation.CheckIn);
        dto.CheckOut.Should().Be(reservation.CheckOut);
        dto.NumberOfGuests.Should().Be(2);
        dto.TotalAmount.Should().Be(600m);
        dto.Currency.Should().Be("EUR");
        dto.Status.Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public void ReservationSummaryDto_FromEntity_FlattensPropertyName()
    {
        var reservation = CreateReservation();
        var property = new Showcase.Catalog.Entities.Property();
        property.SetName("Grand Hotel");
        reservation.Property = property;

        var dto = ReservationSummaryDto.FromEntity(reservation);

        dto.PropertyName.Should().Be("Grand Hotel");
    }

    [Fact]
    public void ReservationSummaryDto_FromEntity_FlattensGuestNames()
    {
        var reservation = CreateReservation();
        var guest = new Guest();
        guest.SetFirstName("John");
        guest.SetLastName("Doe");
        reservation.Guest = guest;

        var dto = ReservationSummaryDto.FromEntity(reservation);

        dto.GuestFirstName.Should().Be("John");
        dto.GuestLastName.Should().Be("Doe");
    }

    [Fact]
    public void ReservationSummaryDto_Projection_IsNotNull()
    {
        ReservationSummaryDto.Projection.Should().NotBeNull();
    }

    private static Reservation CreateReservation()
    {
        var reservation = Reservation.Create(
            guestId: Guid.NewGuid(),
            propertyId: Guid.NewGuid(),
            roomTypeId: Guid.NewGuid(),
            checkIn: DateTimeOffset.UtcNow.AddDays(1),
            checkOut: DateTimeOffset.UtcNow.AddDays(3),
            numberOfGuests: 2,
            totalAmount: 600m,
            currency: "EUR");

        // Navigation properties required by generated FromEntity mapper
        var property = new Showcase.Catalog.Entities.Property();
        property.SetName("Test Property");
        reservation.Property = property;

        var guest = new Guest();
        guest.SetFirstName("Test");
        guest.SetLastName("Guest");
        reservation.Guest = guest;

        return reservation;
    }
}
