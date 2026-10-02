using Pragmatic.Messaging.Attributes;
using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Core.Tests.Sagas;

/// <summary>
///     A process that waits for two acknowledgements, in either order — the shape of Warehouse's
///     compensation, where Stock and Shipping answer on two queues at once.
/// </summary>
[Saga<Delivery>]
public partial class TwoAcknowledgementsSaga : ISaga<Delivery>
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public Delivery State { get; set; }

    /// <inheritdoc />
    public string CorrelationId { get; set; } = "";

    /// <inheritdoc />
    public DateTimeOffset StartedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The first acknowledgement is in.</summary>
    public bool FirstIn { get; set; }

    /// <summary>The second acknowledgement is in.</summary>
    public bool SecondIn { get; set; }

    /// <summary>What the process was started for.</summary>
    public string StartedFor { get; set; } = "";

    /// <summary>The process begins.</summary>
    [SagaStart]
    [InState(Delivery.AwaitingBoth)]
    public void WhenStarted(DeliveryStarted started) => StartedFor = started.CorrelationId;

    /// <summary>The first acknowledgement.</summary>
    [InState(Delivery.AwaitingBoth)]
    public void WhenTheFirstArrives(FirstAcknowledged first)
    {
        FirstIn = true;
        FinishIfBothIn();
    }

    /// <summary>The second acknowledgement.</summary>
    [InState(Delivery.AwaitingBoth)]
    public void WhenTheSecondArrives(SecondAcknowledged second)
    {
        SecondIn = true;
        FinishIfBothIn();
    }

    /// <summary>A step that fails on its own.</summary>
    [InState(Delivery.AwaitingBoth)]
    public void WhenABrokenOneArrives(BrokenAcknowledged broken)
        => throw new InvalidOperationException($"the step itself failed for {StartedFor}");

    private void FinishIfBothIn()
    {
        if (FirstIn && SecondIn)
            State = Delivery.Done;
    }
}
