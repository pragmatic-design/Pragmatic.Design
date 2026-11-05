using Pragmatic.Messaging.Saga;

namespace PragmaticSagas.Tests;

/// <summary>A stay is booked: the saga starts.</summary>
public sealed record StayBooked(Guid StayId) : ICorrelatedMessage
{
    public string CorrelationId => StayId.ToString();
}
