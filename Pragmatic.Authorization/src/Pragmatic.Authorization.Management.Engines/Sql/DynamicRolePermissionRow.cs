using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>
///     A <see cref="DynamicRolePermission" /> on the Sql engine: EF Core's table for the entity (<c>DynamicRolePermissions</c>), column for
///     column.
/// </summary>
[Entity]
[Table("DynamicRolePermissions")]
[Pragmatic.Persistence.Entity.Index(nameof(RoleName), nameof(PermissionName), nameof(TenantId))] // the entity's [Index] (#490)
public sealed class DynamicRolePermissionRow
{
    [Key]
    public Guid PersistenceId { get; set; }

    [MaxLength(256)]
    public string RoleName { get; set; } = "";

    [MaxLength(256)]
    public string PermissionName { get; set; } = "";

    [MaxLength(128)]
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
