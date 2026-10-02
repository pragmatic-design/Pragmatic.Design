using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Identity;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Interceptors;

/// <summary>
///     Stamps the owner on rows inserted without one, for entities carrying <c>[HasOwner]</c>.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why this exists.</b> The mutation invoker writes <c>OwnerId</c>, but it is not the only
///         write path. An entity created by an <b>action</b> through a repository —
///         <c>Entity.Create(…)</c> then <c>_repository.Add(…)</c>, which is how a good deal of
///         application code writes — would reach the database with no owner. The ownership filter then
///         hides it from everyone, including the person who has just created it. The only callers who
///         can still see such a row are those holding the bypass permission.
///     </para>
///     <para>
///         ⚠️ Which is why a test holding the bypass permission cannot show it, and neither can one
///         with a single narrow caller, who sees nothing whether it owns the row or not. It takes a list
///         query with two narrow callers to see the owner's own row missing.
///     </para>
///     <para>
///         Auditing solves the same problem in the same place: <see cref="AuditingInterceptor" />
///         stamps <c>CreatedBy</c> at <c>SaveChanges</c>, so it covers every write path instead of one.
///         This is the same move for ownership.
///     </para>
///     <para>
///         <b>Only when empty, and only on insert.</b> A row whose owner was set deliberately — by a
///         mutation, an import attributing rows to their original author, a seed — keeps it. Ownership
///         is never reassigned here, which is also why the entity is reached through
///         <see cref="IOwnershipAssignable" /> rather than through a settable property: the capability
///         to assign belongs to the layer that writes rows, not to anything holding an entity.
///     </para>
/// </remarks>
public sealed class OwnershipInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser? _currentUser;

    /// <summary>
    ///     Creates an interceptor that attributes new rows to <paramref name="currentUser" />.
    /// </summary>
    /// <param name="currentUser">
    ///     The caller to attribute inserts to. <c>null</c> — a worker, a job, a CLI — leaves ownership
    ///     alone, exactly as auditing leaves attribution alone when nobody is signed in.
    /// </param>
    public OwnershipInterceptor(ICurrentUser? currentUser = null)
    {
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AssignOwners(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AssignOwners(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AssignOwners(DbContext? context)
    {
        if (context is null)
            return;

        var userId = _currentUser?.IdOrNull();
        if (userId is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<IOwnershipAssignable>())
        {
            if (entry.State != EntityState.Added)
                continue;

            if (!string.IsNullOrEmpty(entry.Entity.OwnerId))
                continue;

            entry.Entity.AssignOwner(userId);
        }
    }
}
