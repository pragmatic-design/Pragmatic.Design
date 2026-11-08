using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>
///     A <see cref="DynamicRole" /> on the Sql engine: the table EF Core maps the entity to in the importing boundary's database
///     (<c>DynamicRoles</c>), column for column, so a database holds one set of roles whichever engine writes it.
/// </summary>
[Entity]
[Table("DynamicRoles")]
// The entity's [Unique] (#490), Pragmatic's: a package's row is read as an entity is, so the table has the same index on every engine.
[Pragmatic.Persistence.Entity.Unique(nameof(Name), nameof(TenantId))]
public sealed class DynamicRoleRow
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
    public string? TenantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    internal static DynamicRoleRow From(DynamicRole role) => new()
    {
        PersistenceId = role.PersistenceId,
        IsDeleted = role.IsDeleted,
        DeletedAt = role.DeletedAt,
        DeletedBy = role.DeletedBy,
        Name = role.Name,
        Description = role.Description,
        TenantId = role.TenantId,
        CreatedAt = role.CreatedAt,
        CreatedBy = role.CreatedBy,
        UpdatedAt = role.UpdatedAt,
        UpdatedBy = role.UpdatedBy,
    };

    internal DynamicRole ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        IsDeleted = IsDeleted,
        DeletedAt = DeletedAt,
        DeletedBy = DeletedBy,
        Name = Name,
        Description = Description,
        TenantId = TenantId,
        CreatedAt = CreatedAt,
        CreatedBy = CreatedBy,
        UpdatedAt = UpdatedAt,
        UpdatedBy = UpdatedBy,
    };
}
