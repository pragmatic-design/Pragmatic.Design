using Pragmatic.Messaging.Saga;

namespace PragmaticSagas.Tests;

/// <summary>The hotel answers: accepted, the saga goes on; refused, the step rejects and the saga is compensated.</summary>
public sealed record StayConfirmed(Guid StayId, bool Accepted) : ICorrelatedMessage
{
    public string CorrelationId => StayId.ToString();
}
