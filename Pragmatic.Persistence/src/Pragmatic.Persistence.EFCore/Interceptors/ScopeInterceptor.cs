using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Persistence.EFCore.Scopes;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Interceptors;

/// <summary>
///     Writes the <c>AccessScopes</c> of rows being saved, for entities carrying
///     <c>[HasAccessScopes]</c>: the caller's own scope on a row inserted without one, then the data
///     scope rules that match it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why this exists.</b> Nothing wrote <c>AccessScopes</c>. The attribute produced the column,
///         the <c>GrantScope</c>/<c>RevokeScope</c> pair and a filter reading
///         <c>entity.AccessScopes.Any(s =&gt; userScopes.Contains(s))</c> — and no interceptor, invoker or
///         hook ever put anything in that list, so the filter was <b>false for every row and every
///         caller</b>. A scoped entity was invisible to everyone, including the person who had just
///         created it; only the bypass permission could see it.
///     </para>
///     <para>
///         Nothing reported it for the same reason ownership's version of this went unreported: the
///         Showcase's default test permissions include the bypass, so the scoped tests passed — one
///         through the bypass, and one because a narrow caller sees nothing whether the row is scoped to
///         it or not. Measured through the reservation-to-invoice flow, the invoice raised by user A's
///         own reservation reached the database with an empty scope list.
///     </para>
///     <para>
///         <see cref="OwnershipInterceptor" /> is the same move for <c>[HasOwner]</c>, and
///         <c>AuditingInterceptor</c> is the same move for attribution. All three stamp at
///         <c>SaveChanges</c> so they cover every write path rather than the one that goes through a
///         mutation invoker.
///     </para>
///     <para>
///         <b>Only when empty, and only on insert.</b> A row whose audience was stated — a mutation that
///         granted a team scope, an import attributing rows to the department that owned them, a seed —
///         keeps what it was given. And an update never re-scopes: otherwise the last person to touch a
///         row would quietly acquire it and everyone who could see it before would lose it.
///     </para>
/// </remarks>
public sealed class ScopeInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser? _currentUser;
    private readonly IReadOnlyList<IScopeMaterializationStep> _materialization;

    /// <summary>
    ///     Creates an interceptor that scopes new rows to <paramref name="currentUser" /> and then
    ///     materializes the data scope rules over what is being saved.
    /// </summary>
    /// <param name="currentUser">
    ///     The caller whose scope new rows carry. <c>null</c> — a recurring job, a message off a bus, a
    ///     startup seed — leaves the row unscoped, and the row is the system's. That case has to stay
    ///     expressible, and it is what keeps "the caller's scope" from becoming "whatever scope was
    ///     lying around".
    /// </param>
    /// <param name="materialization">
    ///     One step per scoped entity type, emitted by the generator. Empty in an application that
    ///     declares no data scope rule, where the loop runs zero times.
    /// </param>
    public ScopeInterceptor(
        ICurrentUser? currentUser = null,
        IEnumerable<IScopeMaterializationStep>? materialization = null)
    {
        _currentUser = currentUser;
        _materialization = materialization?.ToArray() ?? [];
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyScopes(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyScopes(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    ///     The stamp, then the rules — in this order, and the order is the correctness of it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The stamp writes the creator's scope <b>only when the list is empty</b>. Materializing
    ///         first would fill the list, "empty" would never be true again, and no row would ever carry
    ///         its creator's scope — in silence and with every test still green, because a row nobody can see is not an error anywhere. The two are in one
    ///         method rather than in two interceptors so that the order is read here instead of inferred
    ///         from the sequence they happen to be registered in.
    ///     </para>
    ///     <para>
    ///         They do not otherwise interfere: the stamp writes <c>user:</c>, the materializer adds and
    ///         removes only its own <c>scope:{name}</c>.
    ///     </para>
    /// </remarks>
    private void ApplyScopes(DbContext? context)
    {
        if (context is null)
            return;

        AssignScopes(context);

        foreach (var step in _materialization)
            step.Materialize(context);
    }

    private void AssignScopes(DbContext context)
    {
        var userId = _currentUser?.IdOrNull();
        if (userId is null)
            return;

        // The string has to be the one IUserScopeResolver produces for the same principal, or the row is
        // scoped to nobody. ScopeIdentifiers is where the two sides agree.
        var ownScope = ScopeIdentifiers.ForUser(userId);

        foreach (var entry in context.ChangeTracker.Entries<IScopedEntity>())
        {
            if (entry.State != EntityState.Added)
                continue;

            if (entry.Entity.AccessScopes.Count > 0)
                continue;

            entry.Entity.AccessScopes.Add(ownScope);
        }
    }
}
