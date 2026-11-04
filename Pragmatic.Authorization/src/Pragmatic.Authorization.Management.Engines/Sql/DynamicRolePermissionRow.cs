using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>
///     A <see cref="DynamicRolePermission" /> on the Sql engine: EF Core's table for the entity (<c>DynamicRolePermissions</c>), column for
///     column.
/// </summary>
[Entity]
[Table("DynamicRolePermissions")]
public sealed class DynamicRolePermissionRow
{
    [Key]
    public Guid PersistenceId { get; set; }

    public string RoleName { get; set; } = "";

    public string PermissionName { get; set; } = "";

    public string? TenantId { get; set; }

    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }

    internal static DynamicRolePermissionRow From(DynamicRolePermission grant) => new()
    {
        PersistenceId = grant.PersistenceId,
        RoleName = grant.RoleName,
        PermissionName = grant.PermissionName,
        TenantId = grant.TenantId,
        ValidFrom = grant.ValidFrom,
        ValidTo = grant.ValidTo,
    };

    internal DynamicRolePermission ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        RoleName = RoleName,
        PermissionName = PermissionName,
        TenantId = TenantId,
        ValidFrom = ValidFrom,
        ValidTo = ValidTo,
    };
}
