using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result.Database;
using DeleteBehavior = Pragmatic.Persistence.Entity.DeleteBehavior;

namespace Pragmatic.Persistence.EFCore.SoftDelete;

/// <summary>
///     Applies the <see cref="SoftDeleteReferrerRule" />s of the principals a save soft-deletes: refuses,
///     empties the referrers' key, or soft-deletes them in turn.
/// </summary>
/// <remarks>
///     <para>
///         A referrer counts only while it is live: one already flagged, or deleted in this same save, no
///         longer points at anything a reader can see. The query runs under the context's filters, so a
///         referrer the soft-delete filter hides is not counted either.
///     </para>
///     <para>
///         <b>A cascade carries the principal's instant.</b> A restore brings back the rows flagged with the
///         principal's <c>DeletedAt</c>, so a referrer stamped with an instant of its own could not be restored
///         with it. A referrer soft-deleted by the cascade has its own rules applied in turn.
///     </para>
///     <para>
///         A refusal is thrown as <see cref="PersistenceRuleViolationException" /> carrying
///         <see cref="DbInUseError" />, the 409 a physical delete refused by the database answers.
///     </para>
/// </remarks>
internal static class SoftDeleteReferrers
{
    public static void Apply(DbContext db, IReadOnlyList<EntityEntry> deleting)
    {
        var work = new Queue<EntityEntry>(deleting);
        while (work.Count > 0)
        {
            var entry = work.Dequeue();
            foreach (var relation in RulesOf(entry.Metadata))
                Handle(db, entry, relation, relation.Rule.Referrers(db, entry.Entity), work);
        }
    }

    public static async Task ApplyAsync(DbContext db, IReadOnlyList<EntityEntry> deleting, CancellationToken ct)
    {
        var work = new Queue<EntityEntry>(deleting);
        while (work.Count > 0)
        {
            var entry = work.Dequeue();
            foreach (var relation in RulesOf(entry.Metadata))
            {
                var referrers = await relation.Rule.ReferrersAsync(db, entry.Entity, ct).ConfigureAwait(false);
                Handle(db, entry, relation, referrers, work);
            }
        }
    }

    private static void Handle(
        DbContext db,
        EntityEntry principal,
        (IForeignKey Key, SoftDeleteReferrerRule Rule) relation,
        List<object> referrers,
        Queue<EntityEntry> work)
    {
        var rule = relation.Rule;
        foreach (var referrer in referrers)
        {
            var entry = db.Entry(referrer);
            if (!IsLive(entry))
                continue;

            switch (rule.Behavior)
            {
                case DeleteBehavior.SetNull or DeleteBehavior.ClientSetNull:
                    // Through the tracker, by the key's own properties: a generated key has no public setter,
                    // and the model already knows which columns hold it.
                    foreach (var property in relation.Key.Properties)
                        entry.Property(property).CurrentValue = null;
                    break;

                case DeleteBehavior.Cascade when referrer is ISoftDelete soft:
                    var flagged = (ISoftDelete)principal.Entity;
                    soft.IsDeleted = true;
                    soft.DeletedAt = flagged.DeletedAt;
                    soft.DeletedBy = flagged.DeletedBy;
                    entry.DetectChanges();
                    work.Enqueue(entry);
                    break;

                case DeleteBehavior.Cascade:
                    // The generator refuses this declaration (PRAG0641); a hand-built rule reaching here would
                    // otherwise leave a live row pointing at a hidden one, which is what it was asked to prevent.
                    throw new InvalidOperationException(
                        $"{rule.DependentType.Name} cannot be soft-deleted with {principal.Metadata.ClrType.Name}: it is not [SoftDelete].");

                default:
                    throw new PersistenceRuleViolationException(new DbInUseError
                    {
                        EntityType = principal.Metadata.ClrType.Name,
                        UsedBy = rule.DependentType.Name
                    });
            }
        }
    }

    private static bool IsLive(EntityEntry entry)
        => entry.State is not (EntityState.Deleted or EntityState.Detached)
           && entry.Entity is not ISoftDelete { IsDeleted: true };

    /// <summary>The rules on the relations that point at this type or at a type it derives from, with their key.</summary>
    private static IEnumerable<(IForeignKey Key, SoftDeleteReferrerRule Rule)> RulesOf(IEntityType type)
    {
        for (IEntityType? current = type; current is not null; current = current.BaseType)
        {
            foreach (var foreignKey in current.GetDeclaredReferencingForeignKeys())
            {
                if (foreignKey.FindAnnotation(SoftDeleteReferrerRule.Annotation)?.Value is SoftDeleteReferrerRule rule)
                    yield return (foreignKey, rule);
            }
        }
    }
}
