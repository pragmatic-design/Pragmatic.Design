using Pragmatic.Messaging.Attributes;
using Pragmatic.Messaging.Saga;

namespace PragmaticSagas.Tests;

/// <summary>
///     A stay: booked, confirmed (or refused), checked in. The confirmation opens a ten-minute window for the check-in and is compensated by a
///     cancellation; the saga is kept by its boundary's engine, through the repository the generated registration adds.
/// </summary>
[Saga<StayState>]
public partial class StaySaga : ISaga<StayState>
{
    public Guid Id { get; set; }

    public StayState State { get; set; }

    public string CorrelationId { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The stay, kept so the compensation can be seeded from the saga's state.</summary>
    public Guid StayId { get; set; }

    [SagaStart]
    public ConfirmStay WhenBooked(StayBooked booked)
    {
        StayId = booked.StayId;
        State = StayState.Booked;
        return new ConfirmStay(booked.StayId);
    }

    [InState(StayState.Booked, NextState = StayState.Confirmed)]
    [CompensateWith<CancelStay>]
    [SagaTimeout(Duration = "00:10:00")]
    public PrepareRoom WhenConfirmed(StayConfirmed confirmed)
        => confirmed.Accepted ? new PrepareRoom(StayId) : throw new SagaRejectedException($"the hotel refused stay {StayId}");

    [InState(StayState.Confirmed, NextState = StayState.Done)]
    public WelcomeGuest WhenCheckedIn(GuestCheckedIn checkedIn)
    {
        CompletedAt = DateTimeOffset.UtcNow;
        return new WelcomeGuest(checkedIn.StayId);
    }
}
