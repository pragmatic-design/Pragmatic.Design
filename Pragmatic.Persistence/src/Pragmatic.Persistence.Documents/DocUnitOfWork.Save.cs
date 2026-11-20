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
///     failure stay written (Pragmatic.Persistence/docs/21-engines.md, "What is different").
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

        // The relations' OnSoftDelete, before anything is written: the referrers it changes are written with the principal, and a refusal writes
        // nothing, on Cosmos DB too.
        await ApplySoftDeleteRulesAsync(ct).ConfigureAwait(false);

        var written = _entries.Where(e => e.State != DocEntryState.Unchanged).ToList();
        if (written.Count == 0)
            return await PhasesOnlyAsync(now, userId, ct).ConfigureAwait(false);
        SavePhases.Apply(written, now, userId, _tenant, _requireTenant, CompiledScopeRules.Registry, _scopeRules, _savingHooks);

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

    /// <summary>
    ///     A save with no entity to write: nothing, unless a phase still has documents of its own (an outbox holding an operation's staged events,
    ///     #803). Then only the phases run, in one MongoDB transaction where the server runs them; no entity is written, stamped or published.
    /// </summary>
    /// <remarks>
    ///     Returning before the phases, as every empty save did, left those events to the in-process dispatch after the commit, past the outbox:
    ///     under <c>[Transactional]</c> the steps have saved already and the owner's own save has nothing left to write.
    /// </remarks>
    private async Task<int> PhasesOnlyAsync(DateTimeOffset now, string? userId, CancellationToken ct)
    {
        if (!_phases.Any(p => p.HasPendingWork))
            return 0;

        var own = Session.Transaction is null && await Session.SupportsTransactionsAsync(ct).ConfigureAwait(false) ? Session.BeginTransaction() : null;
        try
        {
            foreach (var phase in _phases)
                await phase.ApplyAsync(new DocSaveContext([], Session, now, userId), ct).ConfigureAwait(false);
            if (own is not null)
                await own.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (own is not null)
                await own.DisposeAsync().ConfigureAwait(false); // not committed: aborted
        }

        return 0;
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
        catch (Exception refused) when (Refusal(refused, type, entry.State == DocEntryState.Added, entry.Entity) is { } violation)
        {
            throw violation;
        }
    }

    /// <summary>MongoDB's code for a document its collection's validator refused.</summary>
    private const int DocumentValidationFailure = 121;

    /// <summary>
    ///     A store's refusal of a write as Pragmatic's broken rule: a stale copy a <see cref="ConcurrencyError" />, a duplicate key (or, on Cosmos
    ///     DB, a taken id of a new document) a conflict, a document the collection's validator refused a constraint error. Null for any other
    ///     failure, which stays as it is.
    /// </summary>
    /// <remarks>
    ///     A duplicate carries the code its index declared (#728), asked of <paramref name="written" />: MongoDB names the index and only the
    ///     first key, so the index is recognized by its name. Cosmos DB names none, so there the code stays the generic one, as it does for a
    ///     bulk write, which has no one document to ask.
    /// </remarks>
    private static PersistenceRuleViolationException? Refusal(Exception refused, string type, bool added, object? written = null)
    {
        switch (refused)
        {
            case DocConcurrencyException stale:
                return new PersistenceRuleViolationException(new ConcurrencyError { Message = stale.Message, EntityTypeName = type }, stale);
            case MongoWriteException { IsDuplicateKey: true } duplicate:
                var (index, field) = DuplicateKey(duplicate.Message);
                // The first key alone is not the index's fields (a tenant's index starts with TenantId): the index's name is, so it is asked by name.
                var conflict = DbConflictError.UniqueViolation(type, field, index);
                var code = written is IDeclaresUniqueErrorCodes declares ? declares.UniqueErrorCode(null, index) : null;
                return new PersistenceRuleViolationException(code is null ? conflict : conflict with { DeclaredCode = code }, duplicate);
            case MongoWriteException { Code: DocumentValidationFailure } invalid:
                // The collection's validator refused the document (a temporal relation's range, #900): a broken rule, as the Sql engine
                // classifies a check it fails (SqlRuleViolations).
                return new PersistenceRuleViolationException(new DbConstraintError { ConstraintType = "Check", TableName = type, Details = invalid.Message }, invalid);
            case CosmosException { IsConflict: true } taken when added:
                // The id is taken in its partition (or a unique key policy refused the document): Cosmos DB names no field.
                return new PersistenceRuleViolationException(DbConflictError.UniqueViolation(type, null, null), taken);
            default:
                return null;
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
