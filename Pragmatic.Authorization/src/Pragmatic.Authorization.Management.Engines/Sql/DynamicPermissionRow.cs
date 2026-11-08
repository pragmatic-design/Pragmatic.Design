using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>
///     A <see cref="DynamicPermission" /> on the Sql engine: EF Core's table for the entity (<c>DynamicPermissions</c>), column for column.
/// </summary>
[Entity]
[Table("DynamicPermissions")]
// The entity's [Unique] (#490), Pragmatic's: a package's row is read as an entity is, so the table has the same index on every engine.
[Pragmatic.Persistence.Entity.Unique(nameof(Name), nameof(TenantId))]
public sealed class DynamicPermissionRow
{
    [Key]
    public Guid PersistenceId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }

    [MaxLength(256)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(128)]
    public string? Category { get; set; }

    [MaxLength(128)]
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
