using Pragmatic.Identity;
using Pragmatic.Persistence.Engines;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The bulk writes of <c>IBulkRepository</c> that go through the unit of work (the update and the delete are compiled where they are called):
///     an insert and an upsert of many documents outside the change tracker, as the Sql engine's, the save's phases applied to them alone.
/// </summary>
public sealed partial class DocUnitOfWork
{
    /// <summary>The instant and the caller a deletion is marked with, as the unit of work marks a soft-deleted entity: what a bulk delete stamps.</summary>
    public (DateTimeOffset At, string? By) DeletionStamp() => (_timeProvider.GetUtcNow(), _currentUser?.IdOrNull());

    /// <summary>
    ///     Inserts <paramref name="documents" /> (on MongoDB in batches, in one transaction where the server runs them), their generated values filled
    ///     and the save's phases applied to them alone (stamps, owner, scopes, tenant), the other changes of the unit of work left for its save. Not
    ///     tracked afterwards, no domain events. A taken key is a conflict, as on a save.
    /// </summary>
    public async Task<int> InsertManyAsync<T>(IReadOnlyList<T> documents, CancellationToken ct = default) where T : class, IEntity, IDocumentEntity<T>
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
            return 0;
        await PreparedAsync(documents, ct).ConfigureAwait(false);
        await InOwnTransactionAsync(() => Session.InsertManyAsync(documents, ct), typeof(T).Name, added: true, ct).ConfigureAwait(false);
        return documents.Count;
    }

    /// <summary>
    ///     Inserts <paramref name="documents" />, or replaces the ones whose key the caller may see (<paramref name="visible" />, the read by key with
    ///     the caller's filters), each prepared as <see cref="InsertManyAsync{T}" /> prepares one. A key the caller may not see (another tenant's,
    ///     another owner's) is inserted, so the store refuses it as taken: a conflict, never another caller's document overwritten. In one
    ///     transaction where the server runs them. Not tracked afterwards, no domain events.
    /// </summary>
    public async Task<int> UpsertManyAsync<T>(IReadOnlyList<T> documents, Func<T, CancellationToken, Task<bool>> visible, CancellationToken ct = default) where T : class, IEntity, IDocumentEntity<T>
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(visible);
        if (documents.Count == 0)
            return 0;
        await PreparedAsync(documents, ct).ConfigureAwait(false);
        await InOwnTransactionAsync(async () =>
        {
            foreach (var document in documents)
            {
                var replaced = await visible(document, ct).ConfigureAwait(false);
                await (replaced ? Session.ReplaceAsync(document, ct) : Session.InsertAsync(document, ct)).ConfigureAwait(false);
            }
        }, typeof(T).Name, added: true, ct).ConfigureAwait(false);
        return documents.Count;
    }

    /// <summary>The documents' generated values filled and the save's phases applied, as new entries the unit of work does not track.</summary>
    private async Task PreparedAsync<T>(IReadOnlyList<T> documents, CancellationToken ct) where T : class
    {
        var now = _timeProvider.GetUtcNow();
        var userId = _currentUser?.IdOrNull();
        var entries = documents.Select(document => new DocEntry(document, DocEntryState.Added)).ToList();
        await SaveSteps.FillGeneratedValuesAsync(entries, _generatedValues, now, userId, ct).ConfigureAwait(false);
        SavePhases.Apply(entries, now, userId, _tenant, _requireTenant, CompiledScopeRules.Registry, _scopeRules);
    }

    /// <summary>The writes in a transaction of their own where the server runs them and the session has none; the store's refusals as Pragmatic's.</summary>
    private async Task InOwnTransactionAsync(Func<Task> writes, string type, bool added, CancellationToken ct)
    {
        var own = Session.Transaction is null && await Session.SupportsTransactionsAsync(ct).ConfigureAwait(false) ? Session.BeginTransaction() : null;
        try
        {
            await writes().ConfigureAwait(false);
            if (own is not null)
                await own.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception refused) when (Refusal(refused, type, added) is { } violation)
        {
            throw violation;
        }
        finally
        {
            if (own is not null)
                await own.DisposeAsync().ConfigureAwait(false); // not committed: aborted
        }
    }
}
