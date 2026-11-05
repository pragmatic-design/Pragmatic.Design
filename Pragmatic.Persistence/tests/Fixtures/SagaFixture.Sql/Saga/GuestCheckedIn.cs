using Pragmatic.Messaging.Saga;

namespace PragmaticSagas.Tests;

/// <summary>The guest arrives: the saga completes.</summary>
public sealed record GuestCheckedIn(Guid StayId) : ICorrelatedMessage
{
    public string CorrelationId => StayId.ToString();
}
