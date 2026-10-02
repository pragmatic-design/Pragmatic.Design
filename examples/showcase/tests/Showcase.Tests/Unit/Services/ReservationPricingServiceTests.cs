using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Persistence.Repository;
using Showcase.Booking.Infrastructure.Services;
using Showcase.Catalog.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Services;

/// <summary>
/// Tests pricing calculation service.
/// </summary>
public class ReservationPricingServiceTests
{
    private readonly ReadRepositoryOfRoomTypeMock _roomTypes = new ReadRepositoryOfRoomTypeMock();
    private readonly ReservationPricingService _sut;

    public ReservationPricingServiceTests()
    {
        _sut = new ReservationPricingService(_roomTypes);
    }

    [Fact]
    public async Task CalculateTotal_WithValidRoomType_ReturnsNightsTimesRate()
    {
        var roomTypeId = Guid.NewGuid();
        var roomType = CreateRoomType(roomTypeId, 100m);
        _roomTypes.GetByIdAsync.When(roomTypeId, Arg.Any<CancellationToken>())
.Returns(roomType);

        var checkIn = DateTimeOffset.UtcNow;
        var checkOut = checkIn.AddDays(3);

        var total = await _sut.CalculateTotalAsync(roomTypeId, checkIn, checkOut);

        total.Should().Be(300m); // 3 nights × 100
    }

    [Fact]
    public async Task CalculateTotal_WithNonExistentRoomType_ReturnsZero()
    {
        _roomTypes.GetByIdAsync.Returns((RoomType?)null);

        var total = await _sut.CalculateTotalAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3));

        total.Should().Be(0m);
    }

    [Fact]
    public async Task CalculateTotal_SingleNight_ReturnsBaseRate()
    {
        var roomTypeId = Guid.NewGuid();
        _roomTypes.GetByIdAsync.When(roomTypeId, Arg.Any<CancellationToken>())
.Returns(CreateRoomType(roomTypeId, 250m));

        var checkIn = DateTimeOffset.UtcNow;
        var checkOut = checkIn.AddDays(1);

        var total = await _sut.CalculateTotalAsync(roomTypeId, checkIn, checkOut);

        total.Should().Be(250m);
    }

    private static RoomType CreateRoomType(Guid id, decimal baseRate)
    {
        var roomType = new RoomType { PersistenceId = id };
        roomType.SetBaseRate(baseRate);
        return roomType;
    }
}
