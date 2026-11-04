using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>
///     A <see cref="DynamicPermission" /> on the Sql engine: EF Core's table for the entity (<c>DynamicPermissions</c>), column for column.
/// </summary>
[Entity]
[Table("DynamicPermissions")]
public sealed class DynamicPermissionRow
{
    [Key]
    public Guid PersistenceId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public string? Category { get; set; }

    public string? TenantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    internal static DynamicPermissionRow From(DynamicPermission permission) => new()
    {
        PersistenceId = permission.PersistenceId,
        IsDeleted = permission.IsDeleted,
        DeletedAt = permission.DeletedAt,
        DeletedBy = permission.DeletedBy,
        Name = permission.Name,
        Description = permission.Description,
        Category = permission.Category,
        TenantId = permission.TenantId,
        CreatedAt = permission.CreatedAt,
        CreatedBy = permission.CreatedBy,
        UpdatedAt = permission.UpdatedAt,
        UpdatedBy = permission.UpdatedBy,
    };

    internal DynamicPermission ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        IsDeleted = IsDeleted,
        DeletedAt = DeletedAt,
        DeletedBy = DeletedBy,
        Name = Name,
        Description = Description,
        Category = Category,
        TenantId = TenantId,
        CreatedAt = CreatedAt,
        CreatedBy = CreatedBy,
        UpdatedAt = UpdatedAt,
        UpdatedBy = UpdatedBy,
    };
}
