using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result.Database;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The relations' <c>OnSoftDelete</c> on the Sql engine (<see cref="SqlSoftDeleteRules" />): for each row the save soft-deletes, the live
///     referrers are refused for, have their key emptied, or are soft-deleted in turn — what EF Core's <c>SoftDeleteInterceptor</c> does.
/// </summary>
/// <remarks>
///     <para>
///         Before the save's statements are built, so what the rules change on the referrers is written with the principal, in its batch and its
///         transaction, and a refusal writes nothing. The referrers are read through their own repository on this unit of work: under the
///         caller's filters, the soft-deleted rows left out, and tracked, so a row already changed in memory is the one the rule sees.
///     </para>
///     <para>
///         <b>A referrer counts only while it is live</b>: one flagged, or deleted in this same save, no longer points at anything a reader can
///         see. <b>A cascade carries the principal's instant</b>, so a restore of the principal can bring it back with it, and the referrer's own
///         rules are applied in turn.
///     </para>
///     <para>
///         Inside <c>SoftDeleteScope.Suspend</c> nothing is soft-deleted (the rows are erased), so nothing here applies.
///     </para>
/// </remarks>
public sealed partial class SqlUnitOfWork
{
    private async Task ApplySoftDeleteRulesAsync(CancellationToken ct)
    {
        if (SoftDeleteScope.IsSuspended)
            return;

        var work = new Queue<SqlEntry>(_entries.Where(e => e.IsSoftDeleting));
        if (work.Count == 0)
            return;

        var applied = new HashSet<SqlEntry>();
        while (work.Count > 0)
        {
            var principal = work.Dequeue();
            if (!applied.Add(principal))
                continue;

            foreach (var rule in SqlSoftDeleteRules.Into(principal.Entity.GetType()))
            {
                var referrers = await rule.Referrers(this, ((IEntity)principal.Entity).PersistenceId, ct).ConfigureAwait(false);
                foreach (var referrer in referrers)
                {
                    if (EntryOf(referrer) is not { } entry || !IsLive(entry))
                        continue;

                    Apply(rule, principal, entry, work);
                }
            }
        }

        // What the rules changed is a change like any other: detected as the save detects it.
        var dialect = Session.Dialect;
        foreach (var entry in _entries)
        {
            if (entry.State == SqlEntryState.Unchanged && entry.ChangedColumns(dialect) != 0)
                entry.State = SqlEntryState.Modified;
        }
    }

    private static void Apply(SqlSoftDeleteRule rule, SqlEntry principal, SqlEntry referrer, Queue<SqlEntry> work)
    {
        switch (rule.Behavior)
        {
            case DeleteBehavior.SetNull or DeleteBehavior.ClientSetNull:
                (rule.ClearKey ?? throw new InvalidOperationException(
                    $"The OnSoftDelete rule from {rule.DependentType.Name} to {rule.PrincipalType.Name} empties a key it has no way to write."))(referrer.Entity);
                break;

            case DeleteBehavior.Cascade when referrer.Entity is ISoftDelete soft:
                var flagged = (ISoftDelete)principal.Entity;
                soft.IsDeleted = true;
                soft.DeletedAt = flagged.DeletedAt;
                soft.DeletedBy = flagged.DeletedBy;
                work.Enqueue(referrer);
                break;

            case DeleteBehavior.Cascade:
                // The generator refuses this declaration (PRAG0641); a hand-registered rule reaching here would otherwise leave a live row
                // pointing at a hidden one, which is what it was asked to prevent.
                throw new InvalidOperationException(
                    $"{rule.DependentType.Name} cannot be soft-deleted with {rule.PrincipalType.Name}: it is not [SoftDelete].");

            default:
                throw new PersistenceRuleViolationException(new DbInUseError
                {
                    EntityType = principal.Entity.GetType().Name,
                    UsedBy = rule.DependentType.Name
                });
        }
    }

    private static bool IsLive(SqlEntry entry)
        => entry.State != SqlEntryState.Deleted && entry.Entity is not ISoftDelete { IsDeleted: true };
}
