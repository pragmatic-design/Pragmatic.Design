using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Core.Tests.Sagas;

/// <summary>Starts a <see cref="TwoAcknowledgementsSaga" />.</summary>
public sealed record DeliveryStarted(string CorrelationId) : ICorrelatedMessage;
