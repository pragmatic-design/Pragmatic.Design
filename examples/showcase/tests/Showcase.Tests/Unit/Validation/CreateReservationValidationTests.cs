using Pragmatic.Testing.Assertions;
using Showcase.Booking.Dtos;
using Xunit;

namespace Showcase.Tests.Unit.Validation;

/// <summary>
/// Tests generated validation for CreateReservationRequest.
/// Demonstrates: [FutureDate], [GreaterThanProperty], [Positive], [Range] — all validated at compile-time.
/// </summary>
public class CreateReservationValidationTests
{
    [Fact]
    public void Validate_WithValidRequest_ReturnsSuccess()
    {
        var request = new CreateReservationRequest
        {
            GuestId = Guid.NewGuid(),
            PropertyId = Guid.NewGuid(),
            RoomTypeId = Guid.NewGuid(),
            CheckIn = DateTimeOffset.UtcNow.AddDays(7),
            CheckOut = DateTimeOffset.UtcNow.AddDays(10),
            NumberOfGuests = 2
        };

        var result = request.Validate();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithPastCheckIn_ReturnsFailure()
    {
        var request = new CreateReservationRequest
        {
            GuestId = Guid.NewGuid(),
            PropertyId = Guid.NewGuid(),
            RoomTypeId = Guid.NewGuid(),
            CheckIn = DateTimeOffset.UtcNow.AddDays(-1),
            CheckOut = DateTimeOffset.UtcNow.AddDays(3),
            NumberOfGuests = 2
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithCheckOutBeforeCheckIn_ReturnsFailure()
    {
        var checkIn = DateTimeOffset.UtcNow.AddDays(5);
        var request = new CreateReservationRequest
        {
            GuestId = Guid.NewGuid(),
            PropertyId = Guid.NewGuid(),
            RoomTypeId = Guid.NewGuid(),
            CheckIn = checkIn,
            CheckOut = checkIn.AddDays(-1),
            NumberOfGuests = 2
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithZeroGuests_ReturnsFailure()
    {
        var request = new CreateReservationRequest
        {
            GuestId = Guid.NewGuid(),
            PropertyId = Guid.NewGuid(),
            RoomTypeId = Guid.NewGuid(),
            CheckIn = DateTimeOffset.UtcNow.AddDays(7),
            CheckOut = DateTimeOffset.UtcNow.AddDays(10),
            NumberOfGuests = 0
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithTooManyGuests_ReturnsFailure()
    {
        var request = new CreateReservationRequest
        {
            GuestId = Guid.NewGuid(),
            PropertyId = Guid.NewGuid(),
            RoomTypeId = Guid.NewGuid(),
            CheckIn = DateTimeOffset.UtcNow.AddDays(7),
            CheckOut = DateTimeOffset.UtcNow.AddDays(10),
            NumberOfGuests = 21
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithNegativeGuests_ReturnsFailure()
    {
        var request = new CreateReservationRequest
        {
            GuestId = Guid.NewGuid(),
            PropertyId = Guid.NewGuid(),
            RoomTypeId = Guid.NewGuid(),
            CheckIn = DateTimeOffset.UtcNow.AddDays(7),
            CheckOut = DateTimeOffset.UtcNow.AddDays(10),
            NumberOfGuests = -1
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithMaxGuests_ReturnsSuccess()
    {
        var request = new CreateReservationRequest
        {
            GuestId = Guid.NewGuid(),
            PropertyId = Guid.NewGuid(),
            RoomTypeId = Guid.NewGuid(),
            CheckIn = DateTimeOffset.UtcNow.AddDays(7),
            CheckOut = DateTimeOffset.UtcNow.AddDays(10),
            NumberOfGuests = 20
        };

        var result = request.Validate();

        result.IsSuccess.Should().BeTrue();
    }
}
