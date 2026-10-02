using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Identity;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Interceptors;

/// <summary>
///     Turns a delete of a soft-delete entity into the flag it was supposed to be.
/// </summary>
/// <remarks>
///     <para>
///         The generated repository's <c>Remove</c> already writes the flag, and every path that goes
///         through it was correct. The paths that do not were not: a child leaving its parent's
///         collection is severed by EF and the row is deleted, and so is anything reaching
///         <c>context.Remove</c> directly. <c>[SoftDelete]</c> opens with "instead of being permanently
///         deleted" and made no such distinction, so the guarantee is enforced where every path
///         converges — at save time.
///     </para>
///     <para>
///         <b>It does not re-stamp.</b> Delete and restore write <c>DeletedAt</c> with one instant
///         shared across a cascade, and restore brings back only the children carrying that stamp.
///         Overwriting it here would leave a child that cannot be restored with its parent, so an
///         entity that already carries the flag is left exactly as it is.
///     </para>
///     <para>
///         <b>What it cannot see:</b> <c>ExecuteDelete</c> never reaches the change tracker, so it is
///         not intercepted. PRAG0687 reports that at compile time rather than leaving it to be found
///         in production.
///     </para>
///     <para>
///         Erasure steps that must really delete say so with <see cref="SoftDeleteScope.Suspend" />.
///     </para>
/// </remarks>
public sealed class SoftDeleteInterceptor(TimeProvider timeProvider, ICurrentUser? currentUser = null)
    : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Convert(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Convert(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Convert(DbContext? context)
    {
        if (context is null || SoftDeleteScope.IsSuspended)
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not ISoftDelete soft)
                continue;

            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;

                // Already flagged means the repository ran, with the shared instant a cascade depends on.
                if (soft.IsDeleted)
                    continue;

                Stamp(soft);
                continue;
            }

            // Flagged without an instant: a caller raised the flag itself rather than deleting, and
            // the instant is this interceptor's business on every path. ⚠️ The path that needed it:
            // detaching a soft-deletable dependent of a *required* relationship cannot sever the
            // link — the key cannot be null, so an orphan is a row EF deletes, and that deletion does
            // not survive the mark — so the detach raises the flag and keeps the key. Without this it
            // arrived as Modified, the loop skipped it, and the row carried IsDeleted with no
            // DeletedAt: recoverable, but with nothing to say when, and a restore that reads the
            // cascade instant could not tell it apart.
            if (entry.State == EntityState.Modified && soft is { IsDeleted: true, DeletedAt: null })
                Stamp(soft);
        }
    }

    private void Stamp(ISoftDelete soft)
    {
        soft.IsDeleted = true;
        soft.DeletedAt = timeProvider.GetUtcNow();
        soft.DeletedBy = currentUser?.IdOrNull();
    }
}
