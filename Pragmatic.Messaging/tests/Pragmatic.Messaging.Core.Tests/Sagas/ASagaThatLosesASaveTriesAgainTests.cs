using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Saga;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Core.Tests.Sagas;

/// <summary>
///     A saga step whose save loses to another writer reads the saga again and runs against what
///     is there now; it is not a fault.
/// </summary>
/// <remarks>
///     <para>
///         Two acknowledgements for one saga, handled at once: the second save finds the version moved and
///         throws. The generated orchestrator treated that as any failure — it marked the saga Faulted and
///         rethrew — and RabbitMQ without a dead-letter exchange discards a message that failed. Warehouse's
///         compensation stayed in <c>Compensating</c> with one acknowledgement lost.
///     </para>
///     <para>
///         Through the orchestrator the generator writes for <see cref="TwoAcknowledgementsSaga" />, against a
///         repository whose rival saves inside the losing save, so the collision happens every time.
///     </para>
/// </remarks>
public sealed class ASagaThatLosesASaveTriesAgainTests
{
    private const string Correlation = "delivery-1";

    private readonly RacingSagaRepository _repository = new();

    [Fact]
    public async Task TheLosingStep_RunsAgainOnTheNewState_AndBothAcknowledgementsCount()
    {
        var orchestrator = await StartedAsync();
        _repository.Rival = theirs => theirs.SecondIn = true;

        await orchestrator.HandleEventAsync(new FirstAcknowledged(Correlation), typeof(FirstAcknowledged), Correlation);

        var stored = _repository.Stored;
        (stored.FirstIn, stored.SecondIn, stored.State).Should().Be((true, true, Delivery.Done));
        _repository.Faulted.Should().BeFalse("losing a race is not a fault of the step");
        _repository.SaveAttempts.Should().Be(2, "one lost, one against the state the rival left");
    }

    /// <summary>The control: a step that fails on its own still faults the saga, and the failure still surfaces.</summary>
    [Fact]
    public async Task AStepThatFails_StillFaultsTheSaga()
    {
        var orchestrator = await StartedAsync();

        var handle = () => orchestrator.HandleEventAsync(
            new BrokenAcknowledged(Correlation), typeof(BrokenAcknowledged), Correlation);

        await handle.Should().ThrowAsync<InvalidOperationException>();
        _repository.Faulted.Should().BeTrue();
    }

    /// <summary>
    ///     The bound: a saga that keeps losing gives up with the conflict — it does not spin — and still is not
    ///     marked faulted, because nothing about it is wrong.
    /// </summary>
    [Fact]
    public async Task ASagaThatKeepsLosing_GivesUpWithTheConflict_NotFaulted()
    {
        var orchestrator = await StartedAsync();
        _repository.Rival = theirs => theirs.SecondIn = true;
        _repository.RivalSaves = int.MaxValue;

        var handle = () => orchestrator.HandleEventAsync(
            new FirstAcknowledged(Correlation), typeof(FirstAcknowledged), Correlation);

        await handle.Should().ThrowAsync<SagaConcurrencyException>();
        _repository.Faulted.Should().BeFalse();
        _repository.SaveAttempts.Should().BeLessThan(20, "the retries are bounded");
    }

    private async Task<TwoAcknowledgementsSaga.Orchestrator> StartedAsync()
    {
        var orchestrator = new TwoAcknowledgementsSaga.Orchestrator(
            new TwoAcknowledgementsSaga(), _repository, new SilentBus(),
            NullLogger<TwoAcknowledgementsSaga.Orchestrator>.Instance);
        await orchestrator.HandleEventAsync(new DeliveryStarted(Correlation), typeof(DeliveryStarted), Correlation)
            .ConfigureAwait(false);
        return orchestrator;
    }
}
