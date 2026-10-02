using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Persistence.RollUp;

namespace Pragmatic.Persistence.EFCore.RollUp;

/// <summary>
///     EF Core interceptor that keeps parent roll-up aggregates current (#2). During <c>SavingChanges</c> —
///     before the children are committed, in the same unit of work — it adjusts each affected parent's stored
///     aggregate using the SG-emitted <see cref="RollUpRule"/>s.
/// </summary>
/// <remarks>
///     <para>Handled child transitions:</para>
///     <list type="bullet">
///         <item><description><b>Added</b> → +amount (skipped when inserted already soft-deleted).</description></item>
///         <item><description><b>Deleted</b> → −amount (skipped when the child was already soft-deleted:
///         its amount left the aggregate at soft-delete time).</description></item>
///         <item><description><b>Modified</b> → delta = current − original amount; when the parent key
///         changed (re-parenting) the original amount leaves the old parent and the current amount joins
///         the new one.</description></item>
///         <item><description><b>Soft delete / restore</b> (Modified with the <c>IsDeleted</c> flag
///         flipping) → treated as remove / add respectively.</description></item>
///     </list>
///     <para>
///         <b>Concurrency:</b> on relational providers the delta is applied with a parameterized
///         <c>UPDATE parent SET aggregate = aggregate + @delta WHERE pk = @key</c> executed in the same
///         transaction as the save (an interceptor-owned transaction is opened when none exists and
///         committed/rolled back with the save) — concurrent contexts increment atomically instead of
///         racing on a read-modify-write. A parent INSERTED in the same unit of work gets the delta on
///         its tracked instance (the row does not exist yet). Non-relational providers (InMemory) and
///         parents with composite/unmapped keys fall back to the tracked read-modify-write.
///     </para>
/// </remarks>
public sealed class RollUpInterceptor : SaveChangesInterceptor
{
    private readonly Dictionary<Type, List<RollUpRule>> _rulesByChild;

    // Transactions this interceptor opened because the save had none: committed on SavedChanges,
    // rolled back on SaveChangesFailed. Keyed weakly so a context abandoned mid-save cannot leak
    // (DbTransaction disposal rolls back).
    private static readonly ConditionalWeakTable<DbContext, IDbContextTransaction> OwnedTransactions = new();

    public RollUpInterceptor(IEnumerable<RollUpRule> rules)
        => _rulesByChild = rules
            .GroupBy(r => r.ChildType)
            .ToDictionary(g => g.Key, g => g.ToList());

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            try
            {
                foreach (var plan in PlanDeltas(context))
                {
                    if (plan.Sql is null)
                    {
                        var parent = plan.TrackedParent?.Entity ?? context.Find(plan.Rule.ParentType, plan.ParentKey);
                        if (parent is not null)
                            plan.Rule.ApplyDelta(parent, plan.Delta);
                        continue;
                    }

                    if (context.Database.CurrentTransaction is null && !OwnedTransactions.TryGetValue(context, out _))
                        OwnedTransactions.Add(context, context.Database.BeginTransaction());

                    context.Database.ExecuteSqlRaw(plan.Sql, plan.Delta, plan.ParentKey);
                    SyncTrackedParent(plan);
                }
            }
            catch
            {
                // EF does not raise SaveChangesFailed for a throw inside a SavingChanges interceptor,
                // so an owned transaction opened above would leak until the context is disposed (and on a
                // pooled context, never). Roll it back here before rethrowing.
                RollbackOwnedTransaction(context);
                throw;
            }
        }

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            try
            {
                foreach (var plan in PlanDeltas(context))
                {
                    if (plan.Sql is null)
                    {
                        var parent = plan.TrackedParent?.Entity
                                     ?? await context.FindAsync(plan.Rule.ParentType, [plan.ParentKey], cancellationToken).ConfigureAwait(false);
                        if (parent is not null)
                            plan.Rule.ApplyDelta(parent, plan.Delta);
                        continue;
                    }

                    if (context.Database.CurrentTransaction is null && !OwnedTransactions.TryGetValue(context, out _))
                        OwnedTransactions.Add(context, await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));

                    await context.Database.ExecuteSqlRawAsync(plan.Sql, [plan.Delta, plan.ParentKey], cancellationToken).ConfigureAwait(false);
                    SyncTrackedParent(plan);
                }
            }
            catch
            {
                // See the sync overload: roll back the owned transaction so it does not leak.
                await RollbackOwnedTransactionAsync(context, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    // Roll back and dispose the transaction this interceptor opened, if any, when the rollup phase fails
    // before EF's own commit/failure hooks run. Best-effort: a rollback throw must not mask the
    // original failure being rethrown by the caller.
    private static void RollbackOwnedTransaction(DbContext context)
    {
        if (!OwnedTransactions.TryGetValue(context, out var tx))
            return;
        OwnedTransactions.Remove(context);
        try { tx.Rollback(); }
        catch { /* best-effort: the transaction may already be aborted */ }
        tx.Dispose();
    }

    private static async ValueTask RollbackOwnedTransactionAsync(DbContext context, CancellationToken ct)
    {
        if (!OwnedTransactions.TryGetValue(context, out var tx))
            return;
        OwnedTransactions.Remove(context);
        try { await tx.RollbackAsync(ct).ConfigureAwait(false); }
        catch { /* best-effort: the transaction may already be aborted */ }
        await tx.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { } context && OwnedTransactions.TryGetValue(context, out var tx))
        {
            OwnedTransactions.Remove(context);
            tx.Commit();
            tx.Dispose();
        }

        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && OwnedTransactions.TryGetValue(context, out var tx))
        {
            OwnedTransactions.Remove(context);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            await tx.DisposeAsync().ConfigureAwait(false);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context && OwnedTransactions.TryGetValue(context, out var tx))
        {
            OwnedTransactions.Remove(context);
            tx.Rollback();
            tx.Dispose();
        }

        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && OwnedTransactions.TryGetValue(context, out var tx))
        {
            OwnedTransactions.Remove(context);
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await tx.DisposeAsync().ConfigureAwait(false);
        }

        await base.SaveChangesFailedAsync(eventData, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One planned adjustment: relational when <see cref="Sql"/> is set, tracked otherwise.</summary>
    private sealed record DeltaPlan(RollUpRule Rule, object ParentKey, decimal Delta, string? Sql, EntityEntry? TrackedParent);

    private List<DeltaPlan> PlanDeltas(DbContext context)
    {
        var plans = new List<DeltaPlan>();
        if (_rulesByChild.Count == 0)
            return plans;

        // Merge deltas per (rule, parent) so concurrent-safe SQL runs once per parent.
        var merged = new Dictionary<(RollUpRule Rule, object Key), decimal>();
        // Snapshot: planning may touch the tracker while entries are being enumerated.
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Deleted or EntityState.Modified))
                continue;

            if (!_rulesByChild.TryGetValue(entry.Entity.GetType(), out var rules))
                continue;

            foreach (var rule in rules)
                CollectEntryDelta(entry, rule, merged);
        }

        var isRelational = merged.Count > 0 && context.Database.IsRelational();
        foreach (var ((rule, parentKey), delta) in merged)
        {
            if (delta == 0m)
                continue;

            var plan = BuildPlan(context, rule, parentKey, delta, isRelational);
            if (plan is not null)
                plans.Add(plan);
        }

        return plans;
    }

    private static DeltaPlan? BuildPlan(DbContext context, RollUpRule rule, object parentKey, decimal delta, bool isRelational)
    {
        var entityType = context.Model.FindEntityType(rule.ParentType);
        var primaryKey = entityType?.FindPrimaryKey();
        var tracked = entityType is null ? null : FindTrackedParent(context, entityType, primaryKey, parentKey);

        // A parent being deleted in the same save does not need its aggregate maintained.
        if (tracked is { State: EntityState.Deleted })
            return null;

        // Relational increment requires: relational provider, a mapped table, a single-property PK,
        // a mapped aggregate column, and a parent row that already exists (not Added in this save).
        if (isRelational
            && tracked is not { State: EntityState.Added }
            && entityType?.GetTableName() is { } tableName
            && primaryKey is { Properties.Count: 1 }
            && entityType.FindProperty(rule.AggregatePropertyName) is { } aggregateProperty)
        {
            var store = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            var keyColumn = primaryKey.Properties[0].GetColumnName(store);
            var aggregateColumn = aggregateProperty.GetColumnName(store);
            if (keyColumn is not null && aggregateColumn is not null)
            {
                var sqlHelper = context.GetService<ISqlGenerationHelper>();
                var table = sqlHelper.DelimitIdentifier(tableName, entityType.GetSchema());
                var col = sqlHelper.DelimitIdentifier(aggregateColumn);
                var key = sqlHelper.DelimitIdentifier(keyColumn);
                // {0}/{1} are turned into DbParameters by ExecuteSqlRaw — never inlined.
                var sql = $"UPDATE {table} SET {col} = {col} + {{0}} WHERE {key} = {{1}}";
                return new DeltaPlan(rule, parentKey, delta, sql, tracked);
            }
        }

        // Tracked read-modify-write fallback (parent Added in this save, non-relational provider,
        // composite/unmapped key, or unmapped aggregate property).
        return new DeltaPlan(rule, parentKey, delta, null, tracked);
    }

    /// <summary>
    ///     Keeps a tracked (already-persisted) parent instance coherent with the relational increment:
    ///     the delta is applied in memory, but the property is excluded from EF's own UPDATE so the
    ///     absolute (possibly stale) value never overwrites the atomic increment.
    /// </summary>
    private static void SyncTrackedParent(DeltaPlan plan)
    {
        if (plan.TrackedParent is not { State: EntityState.Modified or EntityState.Unchanged } entry)
            return;

        plan.Rule.ApplyDelta(entry.Entity, plan.Delta);
        entry.Property(plan.Rule.AggregatePropertyName).IsModified = false;
    }

    private static EntityEntry? FindTrackedParent(
        DbContext context, IEntityType entityType, IKey? primaryKey, object parentKey)
    {
        if (primaryKey is not { Properties.Count: 1 })
            return null;

        var keyName = primaryKey.Properties[0].Name;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (!entityType.ClrType.IsInstanceOfType(entry.Entity))
                continue;
            if (Equals(entry.Property(keyName).CurrentValue, parentKey))
                return entry;
        }

        return null;
    }

    private static void CollectEntryDelta(
        EntityEntry entry, RollUpRule rule, Dictionary<(RollUpRule, object), decimal> deltas)
    {
        switch (entry.State)
        {
            case EntityState.Added when !SoftDeleteDetection.IsFlaggedDeleted(entry):
                Add(rule.GetAmount(entry.Entity), rule.GetParentKey(entry.Entity));
                break;

            case EntityState.Deleted when !SoftDeleteDetection.IsFlaggedDeleted(entry):
                Add(-rule.GetAmount(entry.Entity), rule.GetParentKey(entry.Entity));
                break;

            case EntityState.Modified:
                CollectModified();
                break;
        }

        return;

        void CollectModified()
        {
            // Original snapshot: what the aggregate currently accounts for.
            var original = entry.OriginalValues.ToObject();
            var originalAmount = rule.GetAmount(original);
            var originalKey = rule.GetParentKey(original);

            if (SoftDeleteDetection.IsSoftDeleting(entry))
            {
                // Logical delete: reverse what was counted.
                Add(-originalAmount, originalKey);
                return;
            }

            if (SoftDeleteDetection.IsSoftRestoring(entry))
            {
                // Restore: the current amount (re-)joins the aggregate.
                Add(rule.GetAmount(entry.Entity), rule.GetParentKey(entry.Entity));
                return;
            }

            // A child already soft-deleted (and staying deleted) is not part of the aggregate.
            if (SoftDeleteDetection.IsFlaggedDeleted(entry))
                return;

            var currentAmount = rule.GetAmount(entry.Entity);
            var currentKey = rule.GetParentKey(entry.Entity);

            if (!Equals(originalKey, currentKey))
            {
                // Re-parented: leave the old parent, join the new one.
                Add(-originalAmount, originalKey);
                Add(currentAmount, currentKey);
                return;
            }

            Add(currentAmount - originalAmount, currentKey);
        }

        void Add(decimal delta, object parentKey)
        {
            if (delta == 0m)
                return;

            var key = (rule, parentKey);
            deltas[key] = deltas.TryGetValue(key, out var existing) ? existing + delta : delta;
        }
    }
}
