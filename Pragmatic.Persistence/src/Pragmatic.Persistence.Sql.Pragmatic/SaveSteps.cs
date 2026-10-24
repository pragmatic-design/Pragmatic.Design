using Pragmatic.Actions.Commit;
using Pragmatic.Events;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>The steps of a save before and after its phases that no engine changes: the generated values, then the domain events out.</summary>
internal static class SaveSteps
{
    /// <summary>Every empty <c>[GeneratedValue]</c> of the added rows, before the save holds the connection (a sequence asks the database).</summary>
    public static async Task FillGeneratedValuesAsync(IEnumerable<ISaveEntry> entries, IReadOnlyList<IGeneratedValueBinding> bindings, DateTimeOffset now, string? userId, CancellationToken ct)
    {
        if (bindings.Count == 0)
            return;
        var context = new LifecycleContext { Now = now, UserId = userId };
        foreach (var entry in entries)
        {
            if (!entry.IsAdded)
                continue;
            foreach (var binding in bindings)
                await binding.TryFillAsync(entry.Entity, context, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     The domain events of the tracked entities, after the commit: deferred to the batch of the invoker that owns this commit
    ///     (<see cref="CommitScope.CoveringBatch" /> of <paramref name="unitOfWork" />), or dispatched here outside every claim; left on the
    ///     entities when there is neither.
    /// </summary>
    public static async Task PublishEventsAsync(IUnitOfWork unitOfWork, List<object> raisers, IDomainEventDispatcher? dispatcher, CancellationToken ct)
    {
        var batch = CommitScope.CoveringBatch(unitOfWork);
        if (batch is null && dispatcher is null)
            return;
        var taken = new List<IDomainEvent>();
        foreach (var entity in raisers)
        {
            if (entity is IHasDomainEvents { DomainEvents.Count: > 0 } raiser)
            {
                taken.AddRange(raiser.DomainEvents);
                raiser.ClearDomainEvents();
            }
        }

        if (batch is not null)
        {
            foreach (var @event in taken)
                batch.DeferEvent(@event);
        }
        else if (taken.Count > 0)
        {
            using var outsideTheCommit = CommitScope.Suspend();
            await dispatcher!.DispatchAsync(taken, ct).ConfigureAwait(false);
        }
    }
}
