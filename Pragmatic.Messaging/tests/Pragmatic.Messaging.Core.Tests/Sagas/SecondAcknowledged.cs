using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Core.Tests.Sagas;

/// <summary>The other acknowledgement the saga waits for.</summary>
public sealed record SecondAcknowledged(string CorrelationId) : ICorrelatedMessage;
