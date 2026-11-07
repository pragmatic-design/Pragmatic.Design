using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Saga;
using Xunit;

namespace PragmaticSagas.Tests;

/// <summary>
///     <see cref="StaySaga" /> through the orchestrator and the repository the generated <c>AddPragmaticSagas</c> registers, on the engine of the
///     provider's boundary: the same scenario on every database. Each delivery is a scope of its own, as a message handler's is. What the
///     repository's interface does not say (the status, the deadline, the step rows, the outbox) is read from the store by the suite.
/// </summary>
/// <param name="provider">The application: the boundary's session, <c>AddPragmaticSagas</c>, <paramref name="bus" />.</param>
/// <param name="bus">The bus the saga publishes to where a message does not go through the outbox (a compensation).</param>
/// <param name="instance">The instance's status and whether it has a deadline, read from the store.</param>
/// <param name="steps">The step rows of an instance.</param>
/// <param name="outbox">The outbox messages of a correlation.</param>
public sealed class SagaScenario(IServiceProvider provider, SagaBus bus, Func<Guid, Task<(SagaStatus Status, bool HasDeadline)>> instance,
    Func<Guid, Task<int>> steps, Func<string, Task<int>> outbox)
{
    /// <summary>Booked, confirmed twice (the second delivery matches no step and runs nothing), checked in: completed, each step recorded once.</summary>
    public async Task A_saga_runs_through_its_steps_and_a_step_delivered_twice_runs_once()
    {
        var stay = Guid.NewGuid();
        await DeliverAsync(new StayBooked(stay));
        await DeliverAsync(new StayConfirmed(stay, Accepted: true));
        await DeliverAsync(new StayConfirmed(stay, Accepted: true));
        var confirmed = await FindAsync(stay);
        Assert.Equal(StayState.Confirmed, confirmed.State);
        Assert.Equal(["WhenBooked", "WhenConfirmed"], (await RecordedStepsAsync(confirmed.Id)).Order(StringComparer.Ordinal));
        Assert.Equal((SagaStatus.Active, true), await instance(confirmed.Id)); // the confirmation's window is open

        await DeliverAsync(new GuestCheckedIn(stay));
        var done = await FindAsync(stay);
        Assert.Equal((StayState.Done, true), (done.State, done.CompletedAt is not null));
        Assert.Equal((SagaStatus.Completed, false), await instance(done.Id));
        Assert.Equal(3, await steps(done.Id));
        // The boundary is [EnableOutbox]: each step's message went to its outbox, in the step's save, and none to the bus.
        Assert.Equal(3, await outbox(stay.ToString()));
        Assert.Empty(bus.Published);
        Assert.DoesNotContain(await ActiveAsync(), s => s.Id == done.Id);
    }

    /// <summary>The confirmation's window passes: the timeout runner compensates the confirmation and marks the saga timed out.</summary>
    public async Task A_saga_whose_window_passes_is_compensated_and_timed_out()
    {
        var stay = Guid.NewGuid();
        await DeliverAsync(new StayBooked(stay));
        await DeliverAsync(new StayConfirmed(stay, Accepted: true));
        var saga = await FindAsync(stay);

        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISagaRepository<StaySaga>>();
            Assert.Empty(await repository.GetDueForTimeoutAsync(DateTimeOffset.UtcNow)); // ten minutes left
            Assert.Contains(await repository.GetDueForTimeoutAsync(DateTimeOffset.UtcNow.AddHours(1)), s => s.Id == saga.Id);
            foreach (var runner in scope.ServiceProvider.GetServices<ISagaTimeoutRunner>())
                await runner.RunDueTimeoutsAsync(DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None);
        }

        Assert.Equal((SagaStatus.TimedOut, false), await instance(saga.Id));
        // Seeded from the saga's state: its stay, and the required Reason the saga does not have left at its default.
        Assert.Contains(bus.Published, m => m is CancelStay { Reason: null } cancel && cancel.StayId == stay);
        Assert.DoesNotContain(await ActiveAsync(), s => s.Id == saga.Id);
    }

    /// <summary>The hotel refuses: the step rejects, the saga is compensated and stops.</summary>
    public async Task A_saga_whose_step_rejects_is_compensated()
    {
        var stay = Guid.NewGuid();
        await DeliverAsync(new StayBooked(stay));
        await DeliverAsync(new StayConfirmed(stay, Accepted: false));
        var saga = await FindAsync(stay);

        Assert.Equal(StayState.Booked, saga.State);
        Assert.Equal((SagaStatus.Compensated, false), await instance(saga.Id));
        Assert.Equal(["WhenBooked"], await RecordedStepsAsync(saga.Id));
    }

    /// <summary>
    ///     Two writers read the same instance; the first saves, the second is refused, and its step is not recorded. The rival is a second scope's
    ///     repository, saving between this one's read and its save, so the conflict happens every time.
    /// </summary>
    public async Task A_write_from_a_state_another_save_overtook_is_refused()
    {
        var stay = Guid.NewGuid();
        await DeliverAsync(new StayBooked(stay));

        await using var mine = provider.CreateAsyncScope();
        await using var theirs = provider.CreateAsyncScope();
        var myRepository = mine.ServiceProvider.GetRequiredService<ISagaRepository<StaySaga>>();
        var theirRepository = theirs.ServiceProvider.GetRequiredService<ISagaRepository<StaySaga>>();
        var myRead = (await myRepository.FindByCorrelationAsync(stay.ToString()))!;
        var theirRead = (await theirRepository.FindByCorrelationAsync(stay.ToString()))!;

        theirRead.State = StayState.Confirmed;
        await theirRepository.SaveWithStepAsync(theirRead, null, "Theirs");
        myRead.State = StayState.Done;
        await Assert.ThrowsAsync<SagaConcurrencyException>(() => myRepository.SaveWithStepAsync(myRead, null, "Mine"));

        Assert.Equal(StayState.Confirmed, (await FindAsync(stay)).State);
        Assert.Equal(["Theirs", "WhenBooked"], (await RecordedStepsAsync(myRead.Id)).Order(StringComparer.Ordinal));

        // A fresh read is not stale: the same repository saves again.
        var fresh = (await myRepository.FindByCorrelationAsync(stay.ToString()))!;
        fresh.State = StayState.Done;
        await myRepository.SaveAsync(fresh);
        Assert.Equal(StayState.Done, (await FindAsync(stay)).State);
    }

    /// <summary><paramref name="message" /> delivered to the handler the generated registration binds for it, as a transport delivers it.</summary>
    private async Task DeliverAsync<T>(T message) where T : ICorrelatedMessage
    {
        await using var scope = provider.CreateAsyncScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IMessageHandler<T>>());
        await handler.HandleAsync(message, MessageContext.New(message.CorrelationId));
    }

    private async Task<StaySaga> FindAsync(Guid stay)
    {
        await using var scope = provider.CreateAsyncScope();
        var saga = await scope.ServiceProvider.GetRequiredService<ISagaRepository<StaySaga>>().FindByCorrelationAsync(stay.ToString());
        Assert.NotNull(saga);
        return saga;
    }

    private async Task<IReadOnlyList<string>> RecordedStepsAsync(Guid sagaId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISagaRepository<StaySaga>>().GetExecutedStepNamesAsync(sagaId);
    }

    private async Task<IReadOnlyList<StaySaga>> ActiveAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISagaRepository<StaySaga>>().GetActiveAsync();
    }
}
