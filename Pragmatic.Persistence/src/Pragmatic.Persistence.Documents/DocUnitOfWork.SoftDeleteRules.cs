using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result.Database;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The relations' <c>OnSoftDelete</c> on the documents engine (<see cref="DocSoftDeleteRules" />): for each document the save soft-deletes, the
///     live referrers are refused for, have their key emptied, or are soft-deleted in turn — what EF Core's <c>SoftDeleteInterceptor</c> does, and
///     the Sql engine's save.
/// </summary>
/// <remarks>
///     <para>
///         Before the save writes anything, so what the rules change on the referrers is written with the principal, and <b>a refusal writes
///         nothing on either store</b>: a cascade that meets a refusal further down has only changed tracked entities in memory. That holds on
///         Cosmos DB too, which has no transaction; what it does not hold for is a write failing midway (a stale document), as for any save there.
///         The referrers are read through their own repository on this unit of work: under the caller's filters, the soft-deleted documents left
///         out, and tracked, so a document already changed in memory is the one the rule sees.
///     </para>
///     <para>
///         <b>A referrer counts only while it is live</b>: one flagged, or deleted in this same save, no longer points at anything a reader can
///         see. <b>A cascade carries the principal's instant</b>, so a restore of the principal can bring it back with it, and the referrer's own
///         rules are applied in turn.
///     </para>
///     <para>
///         Inside <c>SoftDeleteScope.Suspend</c> nothing is soft-deleted (the documents are erased), so nothing here applies.
///     </para>
/// </remarks>
public sealed partial class DocUnitOfWork
{
    private async Task ApplySoftDeleteRulesAsync(CancellationToken ct)
    {
        if (SoftDeleteScope.IsSuspended)
            return;

        var work = new Queue<DocEntry>(_entries.Where(e => e.IsSoftDeleting));
        if (work.Count == 0)
            return;

        var applied = new HashSet<DocEntry>();
        while (work.Count > 0)
        {
            var principal = work.Dequeue();
            if (!applied.Add(principal))
                continue;

            foreach (var rule in DocSoftDeleteRules.Into(principal.Entity.GetType()))
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
        foreach (var entry in _entries)
        {
            if (entry.State == DocEntryState.Unchanged && entry.ChangedFields() != 0)
                entry.State = DocEntryState.Modified;
        }
    }

    private static void Apply(DocSoftDeleteRule rule, DocEntry principal, DocEntry referrer, Queue<DocEntry> work)
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
                // The generator refuses this declaration (PRAG0641); a hand-registered rule reaching here would otherwise leave a live document
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

    private static bool IsLive(DocEntry entry)
        => entry.State != DocEntryState.Deleted && entry.Entity is not ISoftDelete { IsDeleted: true };
}
