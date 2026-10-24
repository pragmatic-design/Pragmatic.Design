using System.Text.RegularExpressions;
using Pragmatic.Identity;
using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Engines;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result.Database;
using Pragmatic.Result.Http;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The save, as <c>SqlUnitOfWork</c>'s: the generated values filled, the changes detected (an unchanged entity whose fields differ from its
///     snapshot is modified), the phases run, the caller's right to the documents staged without a read checked, then each document written
///     (MongoDB: a <c>$set</c> of its changed fields; Cosmos DB: a replace at the etag it was read with), the modules' phases, the snapshots taken
///     again, the domain events out. A stale document is a <see cref="ConcurrencyError" /> and a duplicate key a conflict, raised as Pragmatic's
///     <see cref="PersistenceRuleViolationException" />. In one MongoDB transaction where the server runs them; otherwise the writes before a
///     failure stay written (persistence/docs/write-surface.md).
/// </summary>
public sealed partial class DocUnitOfWork
{
    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        var userId = _currentUser?.IdOrNull();
        await SaveSteps.FillGeneratedValuesAsync(_entries, _generatedValues, now, userId, ct).ConfigureAwait(false);
        foreach (var entry in _entries)
        {
            if (entry.State == DocEntryState.Unchanged && entry.ChangedFields() != 0)
                entry.State = DocEntryState.Modified;
        }

        var written = _entries.Where(e => e.State != DocEntryState.Unchanged).ToList();
        if (written.Count == 0)
            return 0;
        SavePhases.Apply(written, now, userId, _tenant, _requireTenant, CompiledScopeRules.Registry, _scopeRules);

        var own = Session.Transaction is null && await Session.SupportsTransactionsAsync(ct).ConfigureAwait(false) ? Session.BeginTransaction() : null;
        try
        {
            // The documents staged without a read are checked first (in the save's transaction where there is one): the caller may write only a
            // document it may see.
            foreach (var entry in written.Where(e => e.Visible is not null))
            {
                if (!await entry.Visible!(ct).ConfigureAwait(false))
                    throw new PersistenceRuleViolationException(NotFoundError.For(entry.Entity.GetType().Name, ((IEntity)entry.Entity).PersistenceId.ToString()));
            }

            foreach (var entry in written)
                await WriteAsync(entry, ct).ConfigureAwait(false);
            foreach (var phase in _phases)
                await phase.ApplyAsync(new DocSaveContext(written, Session, now, userId), ct).ConfigureAwait(false);
            if (own is not null)
                await own.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (own is not null)
                await own.DisposeAsync().ConfigureAwait(false); // not committed: aborted
        }

        var raisers = _entries.Select(e => e.Entity).ToList(); // a deleted entity's events too, taken before it is forgotten
        foreach (var entry in written)
        {
            if (entry.State == DocEntryState.Deleted)
                Forget(entry);
            else
                entry.Saved();
        }

        await SaveSteps.PublishEventsAsync(this, raisers, _dispatcher, ct).ConfigureAwait(false);
        return written.Count; // the entities written, as EF Core counts its entries (a phase's own documents not among them)
    }

    /// <summary>One document written as its state says; a stale copy and a duplicate key raised as Pragmatic's broken rules.</summary>
    private async Task WriteAsync(DocEntry entry, CancellationToken ct)
    {
        var type = entry.Entity.GetType().Name;
        try
        {
            switch (entry.State)
            {
                case DocEntryState.Added:
                    await entry.Tracker.InsertAsync(Session, entry.Entity, ct).ConfigureAwait(false);
                    break;
                case DocEntryState.Modified:
                    await entry.Tracker.UpdateAsync(Session, entry.Entity, entry.ChangedFields(), ct).ConfigureAwait(false);
                    break;
                case DocEntryState.Deleted:
                    await entry.Tracker.DeleteAsync(Session, entry.Entity, ct).ConfigureAwait(false);
                    break;
            }
        }
        catch (DocConcurrencyException stale)
        {
            throw new PersistenceRuleViolationException(new ConcurrencyError { Message = stale.Message, EntityTypeName = type }, stale);
        }
        catch (MongoWriteException duplicate) when (duplicate.IsDuplicateKey)
        {
            var (index, field) = DuplicateKey(duplicate.Message);
            throw new PersistenceRuleViolationException(DbConflictError.UniqueViolation(type, field, index), duplicate);
        }
        catch (CosmosException taken) when (taken.IsConflict && entry.State == DocEntryState.Added)
        {
            // The id is taken in its partition (or a unique key policy refused the document): Cosmos DB names no field.
            throw new PersistenceRuleViolationException(DbConflictError.UniqueViolation(type, null, null), taken);
        }
    }

    /// <summary>
    ///     The index and the first field of an E11000 message (<c>… index: Email_1 dup key: { Email: "a@b" }</c>): MongoDB's wording, the only
    ///     place it names them. INVESTIGATE: read from the message, as the Sql engine reads its constraints; the reply's <c>keyPattern</c> is exact.
    /// </summary>
    internal static (string? Index, string? Field) DuplicateKey(string message)
    {
        var match = DuplicateKeyText().Match(message);
        if (!match.Success)
            return (null, null);
        var field = match.Groups["field"].Value;
        return (match.Groups["index"].Value, field == "_id" ? "PersistenceId" : field);
    }

    [GeneratedRegex(@"index: (?<index>\S+) dup key: \{ ?(?<field>[^:\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateKeyText();
}
