using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Authorization.Management.Engines.Documents;

/// <summary>
///     A <see cref="DynamicRolePermission" /> on the documents engine (collection <c>DynamicRolePermissions</c>): its fields, its instants in
///     UTC ticks.
/// </summary>
[Document("DynamicRolePermissions")]
public sealed partial class DynamicRolePermissionDocument
{
    /// <summary>The collection in the importing boundary's <c>{Boundary}DocSchema</c>.</summary>
    public static readonly DocCollectionSchema Schema = new("DynamicRolePermissions", "/id",
        [new DocIndex("IX_DynamicRolePermissions_RoleName_PermissionName_TenantId", ["RoleName", "PermissionName", "TenantId"])], []); // the entity's [Index] (#490)

    [Key]
    public Guid PersistenceId { get; set; }

    public string RoleName { get; set; } = "";

    public string PermissionName { get; set; } = "";

    public string? TenantId { get; set; }

    public long ValidFrom { get; set; }

    public long? ValidTo { get; set; }

    internal static DynamicRolePermissionDocument From(DynamicRolePermission grant) => new()
    {
        PersistenceId = grant.PersistenceId,
        RoleName = grant.RoleName,
        PermissionName = grant.PermissionName,
        TenantId = grant.TenantId,
        ValidFrom = grant.ValidFrom.UtcTicks,
        ValidTo = grant.ValidTo?.UtcTicks,
    };

    internal DynamicRolePermission ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        RoleName = RoleName,
        PermissionName = PermissionName,
        TenantId = TenantId,
        ValidFrom = Instants.Of(ValidFrom),
        ValidTo = Instants.Of(ValidTo),
    };
}
