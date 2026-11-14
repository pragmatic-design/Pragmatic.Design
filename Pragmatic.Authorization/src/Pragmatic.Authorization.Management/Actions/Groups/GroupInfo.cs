namespace Pragmatic.Authorization.Management.Actions.Groups;

/// <summary>A group as <see cref="ListGroups" /> answers it (#726): its name, scope and the roles given to it that are in force.</summary>
/// <param name="Name">The group's name.</param>
/// <param name="Description">The group's description.</param>
/// <param name="TenantId">The group's tenant, or <see langword="null" /> for a global group.</param>
/// <param name="Roles">The roles given to the group and in force now, in the tenant asked or global, ordered by name.</param>
public sealed record GroupInfo(string Name, string? Description, string? TenantId, IReadOnlyList<string> Roles);
