namespace Pragmatic.Messaging.Core.Tests.Sagas;

/// <summary>The states of <see cref="TwoAcknowledgementsSaga" />.</summary>
public enum Delivery
{
    /// <summary>Waiting for both acknowledgements.</summary>
    AwaitingBoth,

    /// <summary>Both are in.</summary>
    Done,
}
