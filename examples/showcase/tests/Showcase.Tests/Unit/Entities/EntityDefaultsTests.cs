using Pragmatic.Testing.Assertions;
using Showcase.Billing.Entities;
using Showcase.Billing.Enums;
using Showcase.Booking.Entities;
using Showcase.Booking.Enums;
using Showcase.Catalog.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests entity default values and interface implementations.
/// Demonstrates: ISoftDelete, default property values.
/// </summary>
public class EntityDefaultsTests
{
    [Fact]
    public void Invoice_DefaultStatus_IsDraft()
    {
        var invoice = new Invoice();
        invoice.Status.Should().Be(InvoiceStatus.Draft);
    }

    [Fact]
    public void Invoice_DefaultCurrency_IsEur()
    {
        var invoice = new Invoice();
        invoice.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Invoice_ISoftDelete_DefaultsToNotDeleted()
    {
        var invoice = new Invoice();
        invoice.IsDeleted.Should().BeFalse();
        invoice.DeletedAt.Should().BeNull();
        invoice.DeletedBy.Should().BeNull();
    }

    [Fact]
    public void Payment_ISoftDelete_DefaultsToNotDeleted()
    {
        var payment = new Payment();
        payment.IsDeleted.Should().BeFalse();
        payment.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Amenity_ISoftDelete_DefaultsToNotDeleted()
    {
        var amenity = new Amenity();
        amenity.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Reservation_DefaultStatus_IsPending()
    {
        var reservation = new Reservation();
        reservation.Status.Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public void RoomType_DefaultMaxOccupancy_IsTwo()
    {
        var roomType = new RoomType();
        roomType.MaxOccupancy.Should().Be(2);
    }

    [Fact]
    public void RoomType_DefaultCurrency_IsEur()
    {
        var roomType = new RoomType();
        roomType.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Guest_DefaultPreferredLanguage_IsEn()
    {
        var guest = new Guest();
        guest.PreferredLanguage.Should().Be("en");
    }

    [Fact]
    public void CancellationPolicy_DefaultHours_Is48()
    {
        var policy = new CancellationPolicy();
        policy.HoursBeforeCheckIn.Should().Be(48);
    }

    [Fact]
    public void Property_Id_DelegatesTo_PersistenceId()
    {
        var property = new Property();
        property.Id.Should().Be(property.PersistenceId);
    }
}
