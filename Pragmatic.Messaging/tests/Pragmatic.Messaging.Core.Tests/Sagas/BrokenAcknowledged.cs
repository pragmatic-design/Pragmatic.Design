using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Core.Tests.Sagas;

/// <summary>An acknowledgement whose step throws: a genuine fault, not a conflict.</summary>
public sealed record BrokenAcknowledged(string CorrelationId) : ICorrelatedMessage;
