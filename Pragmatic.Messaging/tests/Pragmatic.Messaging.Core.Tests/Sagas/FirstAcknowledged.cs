using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Core.Tests.Sagas;

/// <summary>One of the two acknowledgements the saga waits for.</summary>
public sealed record FirstAcknowledged(string CorrelationId) : ICorrelatedMessage;
