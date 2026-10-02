using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Showcase.Booking.Infrastructure.Services;
using Xunit;

namespace Showcase.Tests.Unit.Services;

/// <summary>
///     Tests the decorator chain for IReservationPricingService.
///     Verifies LoggingReservationPricingService wraps and delegates to ReservationPricingService.
/// </summary>
public class DecoratorChainTests
{
    private readonly ReservationPricingServiceMock _innerService = new ReservationPricingServiceMock();
    private readonly LoggerOfLoggingReservationPricingServiceMock _logger = new LoggerOfLoggingReservationPricingServiceMock();
    private readonly LoggingReservationPricingService _sut;

    public DecoratorChainTests()
    {
        _sut = new LoggingReservationPricingService(_innerService, _logger);
    }

    // =========================================================================
    // Delegation: decorator delegates to inner service
    // =========================================================================

    [Fact]
    public async Task CalculateTotal_DelegatesToInnerService()
    {
        var roomTypeId = Guid.NewGuid();
        var checkIn = DateTimeOffset.UtcNow;
        var checkOut = checkIn.AddDays(3);
        _innerService.CalculateTotalAsync.When(roomTypeId, checkIn, checkOut, Arg.Any<CancellationToken>())
.Returns(300m);

        var result = await _sut.CalculateTotalAsync(roomTypeId, checkIn, checkOut);

        result.Should().Be(300m, "Decorator should return the value from the inner service");
        _innerService.CalculateTotalAsync.Received(1, roomTypeId, checkIn, checkOut, Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // Delegation: decorator passes through inner service result unchanged
    // =========================================================================

    [Fact]
    public async Task CalculateTotal_ReturnsInnerServiceResult_Unchanged()
    {
        var roomTypeId = Guid.NewGuid();
        var checkIn = DateTimeOffset.UtcNow;
        var checkOut = checkIn.AddDays(5);
        _innerService.CalculateTotalAsync.When(roomTypeId, checkIn, checkOut, Arg.Any<CancellationToken>())
.Returns(750m);

        var result = await _sut.CalculateTotalAsync(roomTypeId, checkIn, checkOut);

        result.Should().Be(750m);
    }

    // =========================================================================
    // Logging: decorator logs before and after calculation
    // =========================================================================

    [Fact]
    public async Task CalculateTotal_LogsBeforeAndAfterCalculation()
    {
        var roomTypeId = Guid.NewGuid();
        var checkIn = DateTimeOffset.UtcNow;
        var checkOut = checkIn.AddDays(2);
        _innerService.CalculateTotalAsync.When(roomTypeId, checkIn, checkOut, Arg.Any<CancellationToken>())
.Returns(200m);

        await _sut.CalculateTotalAsync(roomTypeId, checkIn, checkOut);

        // Verify logging occurred (at least 2 log calls: before + after)
        _logger.Log.Received(2);
    }

    // =========================================================================
    // Interface: decorator implements IReservationPricingService
    // =========================================================================

    [Fact]
    public void Decorator_ImplementsInterface()
    {
        _sut.Should().BeAssignableTo<IReservationPricingService>(
            "LoggingReservationPricingService should implement IReservationPricingService for DI decoration");
    }

    // =========================================================================
    // Zero: inner service returns zero, decorator propagates
    // =========================================================================

    [Fact]
    public async Task CalculateTotal_InnerReturnsZero_PropagatesZero()
    {
        _innerService.CalculateTotalAsync.Returns(Task.FromResult(0m));

        var result = await _sut.CalculateTotalAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        result.Should().Be(0m);
    }
}
