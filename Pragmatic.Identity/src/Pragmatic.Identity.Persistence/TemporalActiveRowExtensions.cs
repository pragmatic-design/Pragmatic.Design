using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Identity.Persistence.Entities;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Identity.Persistence;

/// <summary>
///     Application-layer enforcement of the "at most one ACTIVE row per key" invariant on temporal
///     Identity join tables (<see cref="UserRole{TKey}"/>, <see cref="UserGroup{TKey}"/>,
///     <see cref="GroupRole"/>, <see cref="RolePermission"/>).
/// </summary>
/// <remarks>
///     <para>
///         On relational providers the invariant is enforced by a filtered unique index
///         (<c>WHERE ValidTo IS NULL</c>, index <c>UX_*_Active</c>). Providers that cannot express a
///         partial index — notably EF Core InMemory, or a context created with no provider — silently
///         degrade that index to a plain, non-unique lookup, so two active rows for the same key are
///         accepted. These helpers close that gap: call one <em>before</em> inserting an active row.
///     </para>
///     <para>
///         "Active" here means <c>ValidTo == null</c>, matching the DB filter exactly (an expired row,
///         with <c>ValidTo</c> set, does not occupy the active slot). This is intentionally narrower
///         than the point-in-time <c>Active()</c> temporal query (<c>ValidFrom &lt;= now &amp;&amp; …</c>):
///         the uniqueness invariant is about the open-ended current row, not point-in-time visibility.
///     </para>
///     <para>
///         This package owns no write path for these join tables (the EF stores are read-only; rows are
///         inserted by the consumer / the successor <c>[PragmaticUser]</c> model), so there is nothing
///         inside the package to wire. Consumers writing an active row should guard it, e.g.:
///         <code>
///         await db.EnsureNoActiveUserRoleAsync(userId, "admin", ct);
///         db.Set&lt;UserRole&lt;Guid&gt;&gt;().Add(new() { UserId = userId, RoleName = "admin", ValidFrom = now });
///         await db.SaveChangesAsync(ct);
///         </code>
///     </para>
/// </remarks>
public static class TemporalActiveRowExtensions
{
    // Mirrors the member requirements of DbContext.Set<TEntity>() so the trimmer/AOT is satisfied
    // when the entity type flows through the generic HasActiveRowAsync/EnsureNoActiveRowAsync helpers.
    private const DynamicallyAccessedMemberTypes EntityMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties
        | DynamicallyAccessedMemberTypes.Interfaces;

    /// <summary>
    ///     Narrows a temporal-relation query to its currently-active rows (<c>ValidTo IS NULL</c>) and
    ///     returns whether any exist. Composable primitive behind the typed helpers.
    /// </summary>
    public static Task<bool> AnyActiveAsync<T>(this IQueryable<T> query, CancellationToken ct = default)
        where T : class, ITemporalRelation
        => query.AnyAsync(r => r.ValidTo == null, ct);

    /// <summary>
    ///     Returns whether an active (<c>ValidTo IS NULL</c>) row already exists for the key described
    ///     by <paramref name="keyPredicate"/>. Generic escape hatch for keys the typed helpers don't cover.
    /// </summary>
    /// <param name="db">The DbContext holding the temporal set.</param>
    /// <param name="keyPredicate">Predicate selecting the key (e.g. <c>ur =&gt; ur.UserId == id &amp;&amp; ur.RoleName == role</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task<bool> HasActiveRowAsync<
        [DynamicallyAccessedMembers(EntityMembers)] T>(
        this DbContext db,
        Expression<Func<T, bool>> keyPredicate,
        CancellationToken ct = default)
        where T : class, ITemporalRelation
        => db.Set<T>().Where(keyPredicate).AnyActiveAsync(ct);

    /// <summary>
    ///     Throws <see cref="TemporalActiveRowConflictException"/> if an active row already exists for
    ///     the key described by <paramref name="keyPredicate"/>. Call before inserting an active row.
    /// </summary>
    public static async Task EnsureNoActiveRowAsync<
        [DynamicallyAccessedMembers(EntityMembers)] T>(
        this DbContext db,
        Expression<Func<T, bool>> keyPredicate,
        string conflictDescription,
        CancellationToken ct = default)
        where T : class, ITemporalRelation
    {
        if (await db.HasActiveRowAsync(keyPredicate, ct).ConfigureAwait(false))
            throw new TemporalActiveRowConflictException(conflictDescription);
    }

    // --- Typed convenience helpers for the four Identity join tables ---

    /// <summary>Whether an active user→role assignment exists for <paramref name="userId"/>+<paramref name="roleName"/>.</summary>
    public static Task<bool> HasActiveUserRoleAsync<TKey>(
        this DbContext db, TKey userId, string roleName, CancellationToken ct = default)
        where TKey : notnull
        => db.HasActiveRowAsync<UserRole<TKey>>(
            ur => ur.UserId.Equals(userId) && ur.RoleName == roleName, ct);

    /// <summary>Throws if an active user→role assignment already exists for the key. Call before inserting one.</summary>
    public static Task EnsureNoActiveUserRoleAsync<TKey>(
        this DbContext db, TKey userId, string roleName, CancellationToken ct = default)
        where TKey : notnull
        => db.EnsureNoActiveRowAsync<UserRole<TKey>>(
            ur => ur.UserId.Equals(userId) && ur.RoleName == roleName,
            $"UserRole(user={userId}, role={roleName})", ct);

    /// <summary>Whether an active user→group membership exists for <paramref name="userId"/>+<paramref name="groupName"/>.</summary>
    public static Task<bool> HasActiveUserGroupAsync<TKey>(
        this DbContext db, TKey userId, string groupName, CancellationToken ct = default)
        where TKey : notnull
        => db.HasActiveRowAsync<UserGroup<TKey>>(
            ug => ug.UserId.Equals(userId) && ug.GroupName == groupName, ct);

    /// <summary>Throws if an active user→group membership already exists for the key. Call before inserting one.</summary>
    public static Task EnsureNoActiveUserGroupAsync<TKey>(
        this DbContext db, TKey userId, string groupName, CancellationToken ct = default)
        where TKey : notnull
        => db.EnsureNoActiveRowAsync<UserGroup<TKey>>(
            ug => ug.UserId.Equals(userId) && ug.GroupName == groupName,
            $"UserGroup(user={userId}, group={groupName})", ct);

    /// <summary>Whether an active group→role mapping exists for <paramref name="groupName"/>+<paramref name="roleName"/>.</summary>
    public static Task<bool> HasActiveGroupRoleAsync(
        this DbContext db, string groupName, string roleName, CancellationToken ct = default)
        => db.HasActiveRowAsync<GroupRole>(
            gr => gr.GroupName == groupName && gr.RoleName == roleName, ct);

    /// <summary>Throws if an active group→role mapping already exists for the key. Call before inserting one.</summary>
    public static Task EnsureNoActiveGroupRoleAsync(
        this DbContext db, string groupName, string roleName, CancellationToken ct = default)
        => db.EnsureNoActiveRowAsync<GroupRole>(
            gr => gr.GroupName == groupName && gr.RoleName == roleName,
            $"GroupRole(group={groupName}, role={roleName})", ct);

    /// <summary>Whether an active role→permission mapping exists for <paramref name="roleName"/>+<paramref name="permissionName"/>.</summary>
    public static Task<bool> HasActiveRolePermissionAsync(
        this DbContext db, string roleName, string permissionName, CancellationToken ct = default)
        => db.HasActiveRowAsync<RolePermission>(
            rp => rp.RoleName == roleName && rp.PermissionName == permissionName, ct);

    /// <summary>Throws if an active role→permission mapping already exists for the key. Call before inserting one.</summary>
    public static Task EnsureNoActiveRolePermissionAsync(
        this DbContext db, string roleName, string permissionName, CancellationToken ct = default)
        => db.EnsureNoActiveRowAsync<RolePermission>(
            rp => rp.RoleName == roleName && rp.PermissionName == permissionName,
            $"RolePermission(role={roleName}, permission={permissionName})", ct);
}
