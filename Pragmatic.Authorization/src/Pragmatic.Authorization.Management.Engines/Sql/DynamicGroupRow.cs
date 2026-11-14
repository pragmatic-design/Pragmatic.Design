using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>
///     A <see cref="DynamicGroup" /> on the Sql engine: EF Core's table for the entity (<c>DynamicGroups</c>), column for column.
/// </summary>
[Entity]
[Table("DynamicGroups")]
[Pragmatic.Persistence.Entity.Unique(nameof(Name), nameof(TenantId))] // the entity's [Unique], as DynamicRoleRow carries the role's
public sealed class DynamicGroupRow
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

    internal static DynamicGroupRow From(DynamicGroup group) => new()
    {
        PersistenceId = group.PersistenceId,
        IsDeleted = group.IsDeleted,
        DeletedAt = group.DeletedAt,
        DeletedBy = group.DeletedBy,
        Name = group.Name,
        Description = group.Description,
        TenantId = group.TenantId,
        CreatedAt = group.CreatedAt,
        CreatedBy = group.CreatedBy,
        UpdatedAt = group.UpdatedAt,
        UpdatedBy = group.UpdatedBy,
    };

    internal DynamicGroup ToEntity() => new()
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
