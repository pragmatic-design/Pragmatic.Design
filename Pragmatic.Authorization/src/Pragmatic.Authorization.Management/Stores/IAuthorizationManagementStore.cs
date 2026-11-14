using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Composition.Attributes;

namespace Pragmatic.Authorization.Management.Stores;

/// <summary>
///     The package's rows on the engine of the boundary that imported it: what the management actions, the permission provider and the
///     dynamic stores read and write, whichever engine persists them.
/// </summary>
/// <remarks>
///     <para>
///         Each engine has its implementation in the package of that engine (<c>Pragmatic.Authorization.Management.EFCore</c>,
///         <c>Pragmatic.Authorization.Management.Engines</c>), on the same tables: the importing boundary's generated registration adds the one
///         its engine needs. The package itself names no engine, so a module on any of the three can import it.
///     </para>
///     <para>
///         The reads return a user's, a role's or a tenant's rows; whether a temporal row is in force is decided by the caller, at the instant
///         it reads (<see cref="TemporalValidity" />). Those sets are a user's assignments or a role's permissions, small enough to read whole,
///         and one rule decided in one place is what keeps the three engines answering alike.
///     </para>
///     <para>
///         A tenant here is the row's <c>TenantId</c>, never an ambient filter: a null tenant asks for the global rows only, a tenant for its
///         own rows and the global ones.
///     </para>
/// </remarks>
[ProvidedByHost(Lifetime.Scoped)]
public interface IAuthorizationManagementStore
{
    /// <summary>The roles not deleted, global or of <paramref name="tenantId" />.</summary>
    ValueTask<IReadOnlyList<DynamicRole>> GetRolesAsync(string? tenantId, CancellationToken ct = default);

    /// <summary>Stores a new role, stamped as created now by the caller.</summary>
    ValueTask AddRoleAsync(DynamicRole role, CancellationToken ct = default);

    /// <summary>
    ///     Writes a role read through <see cref="GetRolesAsync" /> back, with what the caller changed on it: its description, whether it is
    ///     deactivated (#725), who changed it and when.
    /// </summary>
    ValueTask UpdateRoleAsync(DynamicRole role, CancellationToken ct = default);

    /// <summary>The permissions not deleted, global or of <paramref name="tenantId" />.</summary>
    ValueTask<IReadOnlyList<DynamicPermission>> GetPermissionsAsync(string? tenantId, CancellationToken ct = default);

    /// <summary>Stores a new permission, stamped as created now by the caller.</summary>
    ValueTask AddPermissionAsync(DynamicPermission permission, CancellationToken ct = default);

    /// <summary>The grants of <paramref name="roleNames" />, global or of <paramref name="tenantId" />, in force or not.</summary>
    ValueTask<IReadOnlyList<DynamicRolePermission>> GetRolePermissionsAsync(
        IReadOnlyCollection<string> roleNames, string? tenantId, CancellationToken ct = default);

    /// <summary>Stores new grants, in one write.</summary>
    ValueTask AddRolePermissionsAsync(IReadOnlyList<DynamicRolePermission> grants, CancellationToken ct = default);

    /// <summary>
    ///     Ends <paramref name="grants" /> at <paramref name="validTo" /> (#725): the rows stay, as the history of what the role granted.
    /// </summary>
    ValueTask EndRolePermissionsAsync(IReadOnlyList<DynamicRolePermission> grants, DateTimeOffset validTo, CancellationToken ct = default);

    /// <summary>The role assignments of <paramref name="userId" />, global or of <paramref name="tenantId" />, in force or not.</summary>
    ValueTask<IReadOnlyList<UserRoleAssignment>> GetUserRolesAsync(string userId, string? tenantId, CancellationToken ct = default);

    /// <summary>The assignments of <paramref name="roleName" /> in every tenant, in force or not: the users whose permissions it changes.</summary>
    ValueTask<IReadOnlyList<UserRoleAssignment>> GetRoleHoldersAsync(string roleName, CancellationToken ct = default);

    /// <summary>Stores a new role assignment.</summary>
    ValueTask AddUserRoleAsync(UserRoleAssignment assignment, CancellationToken ct = default);

    /// <summary>Ends <paramref name="assignment" /> at <paramref name="validTo" />: the row stays, as the history of who held the role.</summary>
    ValueTask EndUserRoleAsync(UserRoleAssignment assignment, DateTimeOffset validTo, CancellationToken ct = default);

    /// <summary>The group assignments of <paramref name="userId" />, global or of <paramref name="tenantId" />, in force or not.</summary>
    ValueTask<IReadOnlyList<UserGroupAssignment>> GetUserGroupsAsync(string userId, string? tenantId, CancellationToken ct = default);

    /// <summary>The members of <paramref name="groupName" /> in every tenant, in force or not: the users whose permissions it changes (#726).</summary>
    ValueTask<IReadOnlyList<UserGroupAssignment>> GetGroupMembersAsync(string groupName, CancellationToken ct = default);

    /// <summary>Stores a new group membership (#726).</summary>
    ValueTask AddUserGroupAsync(UserGroupAssignment assignment, CancellationToken ct = default);

    /// <summary>Ends <paramref name="assignment" /> at <paramref name="validTo" /> (#726): the row stays, as the history of who was a member.</summary>
    ValueTask EndUserGroupAsync(UserGroupAssignment assignment, DateTimeOffset validTo, CancellationToken ct = default);

    /// <summary>The groups not deleted, global or of <paramref name="tenantId" /> (#726).</summary>
    ValueTask<IReadOnlyList<DynamicGroup>> GetGroupsAsync(string? tenantId, CancellationToken ct = default);

    /// <summary>Stores a new group, stamped as created now by the caller (#726).</summary>
    ValueTask AddGroupAsync(DynamicGroup group, CancellationToken ct = default);

    /// <summary>The roles given to <paramref name="groupNames" />, global or of <paramref name="tenantId" />, in force or not (#726).</summary>
    ValueTask<IReadOnlyList<GroupRoleAssignment>> GetGroupRolesAsync(
        IReadOnlyCollection<string> groupNames, string? tenantId, CancellationToken ct = default);

    /// <summary>The groups <paramref name="roleName" /> was given to, in every tenant, in force or not: whose members it reaches (#726).</summary>
    ValueTask<IReadOnlyList<GroupRoleAssignment>> GetRoleGroupsAsync(string roleName, CancellationToken ct = default);

    /// <summary>Stores a new role of a group (#726).</summary>
    ValueTask AddGroupRoleAsync(GroupRoleAssignment assignment, CancellationToken ct = default);

    /// <summary>Ends <paramref name="assignment" /> at <paramref name="validTo" /> (#726): the row stays, as the history of what the group held.</summary>
    ValueTask EndGroupRoleAsync(GroupRoleAssignment assignment, DateTimeOffset validTo, CancellationToken ct = default);
}
