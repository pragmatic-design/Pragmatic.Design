using Pragmatic.Identity;
using Pragmatic.Persistence.Engines;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result.Http;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The save: the generated values filled, the changes detected (an unchanged entity whose columns differ from its snapshot is modified), the
///     phases run, the caller's right to the rows staged without a read checked, every change written in one batch in one transaction, the
///     snapshots taken again, the domain events out. A stale row is a <see cref="ConcurrencyError" />, raised as Pragmatic's
///     <see cref="PersistenceRuleViolationException" /> (owner's decision of 2026-10-22), and so is a rule the schema enforces
///     (<see cref="SqlRuleViolations" />).
/// </summary>
public sealed partial class SqlUnitOfWork
{
    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        var userId = _currentUser?.IdOrNull();
        DetectGraph(); // first: a child added to a collection is a new row, and gets its generated values as any other
        await SaveSteps.FillGeneratedValuesAsync(_entries, _generatedValues, now, userId, ct).ConfigureAwait(false);
        var dialect = Session.Dialect;
        foreach (var entry in _entries)
        {
            if (entry.State == SqlEntryState.Unchanged && entry.ChangedColumns(dialect) != 0)
                entry.State = SqlEntryState.Modified;
        }

        var written = _entries.Where(e => e.State != SqlEntryState.Unchanged).ToList();
        var links = DetectLinks();
        if (written.Count == 0 && links.Count == 0)
            return 0;
        var added = written.Where(e => e.State == SqlEntryState.Added).ToList();
        SavePhases.Apply(written, now, userId, _tenant, _requireTenant, CompiledScopeRules.Registry, _scopeRules);

        // The rows staged without a read are checked in the save's transaction: the caller may write only a row it may see.
        var checks = written.Where(e => e.Visible is not null).ToList();
        var own = Session.Transaction is null && checks.Count > 0 ? Session.BeginTransaction() : null;
        int rows;
        try
        {
            foreach (var entry in checks)
            {
                if (!await entry.Visible!(ct).ConfigureAwait(false))
                    throw new PersistenceRuleViolationException(NotFoundError.For(entry.Entity.GetType().Name, ((IEntity)entry.Entity).PersistenceId.ToString()));
            }

            var batch = Session.WriteBatch();
            batch.SoftDeleteAsIs = SoftDeleteScope.IsSuspended;
            foreach (var entry in Ordered(written))
            {
                switch (entry.State)
                {
                    case SqlEntryState.Added:
                        batch.InsertAsIs(entry.Entity);
                        break;
                    case SqlEntryState.Modified:
                        batch.UpdateColumns(entry.Entity, entry.ChangedColumns(dialect));
                        break;
                    case SqlEntryState.Deleted:
                        batch.DeleteRow(entry.Entity);
                        break;
                }
            }

            // The links after the rows: a new parent is inserted before its join rows, and the rows a side names exist already.
            foreach (var (add, parent, link, row) in links)
            {
                if (add)
                    batch.LinkRows(parent, link, row);
                else
                    batch.UnlinkRows(parent, link, row);
            }

            foreach (var phase in _phases)
                await phase.ApplyAsync(new SqlSaveContext(written, batch, now, userId), ct).ConfigureAwait(false);
            rows = written.Count; // the entities written, as EF Core counts its entries (a phase's own rows not among them)
            try
            {
                await batch.ExecuteAsync(ct).ConfigureAwait(false);
            }
            catch (SqlConcurrencyException stale)
            {
                throw new PersistenceRuleViolationException(new ConcurrencyError { Message = stale.Message, EntityTypeName = TypeOf(stale.Table) }, stale);
            }
            catch (System.Data.Common.DbException failure)
            {
                // A rule the schema enforces (unique, relation, null) is a broken rule, not a crash; anything else stays the provider's exception.
                if (SqlRuleViolations.Classify(failure, written) is not { } broken)
                    throw;
                throw new PersistenceRuleViolationException(broken, failure);
            }

            own?.Commit();
        }
        finally
        {
            own?.Dispose();
        }

        var raisers = _entries.Select(e => e.Entity).ToList(); // a deleted entity's events too, taken before it is forgotten
        foreach (var entry in written)
        {
            if (entry.State == SqlEntryState.Deleted)
                Forget(entry);
            else
                entry.Saved(dialect);
        }

        foreach (var entry in _entries)
            Remember(entry); // the collections as saved: what the next save compares with
        RememberSavedLinks(added);

        await SaveSteps.PublishEventsAsync(this, raisers, _dispatcher, ct).ConfigureAwait(false);
        return rows;
    }

    /// <summary>
    ///     Inserts <paramref name="rows" /> in one batch, outside the change tracker (as EF Core's bulk insert): their generated values filled and
    ///     the save's phases applied to them alone (stamps, owner, scopes, tenant), the other changes of the unit of work left for its save. Not
    ///     tracked afterwards, no domain events.
    /// </summary>
    public async Task<int> InsertManyAsync<T>(IReadOnlyList<T> rows, CancellationToken ct = default) where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(rows);
        var entries = await PreparedAsync(rows, ct).ConfigureAwait(false);
        if (entries.Count == 0)
            return 0;
        var batch = Session.WriteBatch();
        foreach (var entry in entries)
            batch.InsertAsIs(entry.Entity);
        await batch.ExecuteAsync(ct).ConfigureAwait(false);
        return entries.Count;
    }

    /// <summary>
    ///     Inserts <paramref name="rows" />, or updates the ones whose key the caller may see (<paramref name="visible" />, the read by key with the
    ///     caller's filters), each prepared as <see cref="InsertManyAsync{T}" /> prepares a row, in one transaction of the session. A key the caller
    ///     may not see (another tenant's, another owner's) is inserted, so the database refuses it as taken: a broken rule, never another caller's
    ///     row overwritten. Not tracked afterwards, no domain events.
    /// </summary>
    public async Task<int> UpsertManyAsync<T>(IReadOnlyList<T> rows, Func<T, CancellationToken, Task<bool>> visible, CancellationToken ct = default) where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(visible);
        var entries = await PreparedAsync(rows, ct).ConfigureAwait(false);
        if (entries.Count == 0)
            return 0;
        var own = Session.Transaction is null ? Session.BeginTransaction() : null;
        try
        {
            foreach (var entry in entries)
            {
                var row = (T)entry.Entity;
                await (await visible(row, ct).ConfigureAwait(false) ? Session.UpsertAsync(row, ct) : Session.InsertAsync(row, ct)).ConfigureAwait(false);
            }

            own?.Commit();
        }
        catch (System.Data.Common.DbException failure) when (SqlRuleViolations.Classify(failure, entries) is { } broken)
        {
            throw new PersistenceRuleViolationException(broken, failure);
        }
        finally
        {
            own?.Dispose();
        }

        return entries.Count;
    }

    /// <summary>The rows as new entries, their generated values filled and the save's phases applied, without tracking them.</summary>
    private async Task<List<SqlEntry>> PreparedAsync<T>(IReadOnlyList<T> rows, CancellationToken ct) where T : class
    {
        var now = _timeProvider.GetUtcNow();
        var userId = _currentUser?.IdOrNull();
        var entries = rows.Select(row => new SqlEntry(row, SqlEntryState.Added, Session.Dialect)).ToList();
        await SaveSteps.FillGeneratedValuesAsync(entries, _generatedValues, now, userId, ct).ConfigureAwait(false);
        SavePhases.Apply(entries, now, userId, _tenant, _requireTenant, CompiledScopeRules.Registry, _scopeRules);
        return entries;
    }

    /// <summary>
    ///     The writes in an order the foreign keys accept: the inserts and updates of a referenced type before its referrers', the deletes after
    ///     them (each in the order tracked). A cycle between types keeps the tracked order.
    /// </summary>
    private static List<SqlEntry> Ordered(List<SqlEntry> written)
    {
        var types = written.Select(e => e.Entity.GetType()).Distinct().ToList();
        var ranked = new List<Type>();
        var visiting = new HashSet<Type>();
        void Visit(Type type)
        {
            if (ranked.Contains(type) || !visiting.Add(type))
                return;
            foreach (var navigation in SqlEntityMap.For(type)?.Navigations ?? [])
            {
                if (types.Contains(navigation.Target) && navigation.Target != type)
                    Visit(navigation.Target);
            }

            ranked.Add(type);
        }

        foreach (var type in types)
            Visit(type);
        var rank = ranked.Select((t, i) => (t, i)).ToDictionary(p => p.t, p => p.i);
        var writes = written.Where(e => e.State != SqlEntryState.Deleted).OrderBy(e => rank[e.Entity.GetType()]);
        var deletes = written.Where(e => e.State == SqlEntryState.Deleted).OrderByDescending(e => rank[e.Entity.GetType()]);
        return writes.Concat(deletes).ToList();
    }

    private static string? TypeOf(string table) => SqlEntityMap.All.FirstOrDefault(m => m.Table == table)?.Type.Name;
}
