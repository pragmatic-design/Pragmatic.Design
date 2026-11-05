using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Authorization.Management.Engines.Documents;

/// <summary>A <see cref="DynamicRole" /> on the documents engine (collection <c>DynamicRoles</c>): its fields, its instants in UTC ticks.</summary>
[Document("DynamicRoles")]
public sealed partial class DynamicRoleDocument
{
    /// <summary>The collection in the importing boundary's <c>{Boundary}DocSchema</c>, with the entity's unique index (#490).</summary>
    public static readonly DocCollectionSchema Schema = new("DynamicRoles", "/id", [new DocIndex("IX_DynamicRoles_Name_TenantId", ["Name", "TenantId"], Unique: true)], []);

    [Key]
    public Guid PersistenceId { get; set; }

    public bool IsDeleted { get; set; }

    public long? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public string? TenantId { get; set; }

    public long CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public long? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    internal static DynamicRoleDocument From(DynamicRole role) => new()
    {
        PersistenceId = role.PersistenceId,
        IsDeleted = role.IsDeleted,
        DeletedAt = role.DeletedAt?.UtcTicks,
        DeletedBy = role.DeletedBy,
        Name = role.Name,
        Description = role.Description,
        TenantId = role.TenantId,
        CreatedAt = role.CreatedAt.UtcTicks,
        CreatedBy = role.CreatedBy,
        UpdatedAt = role.UpdatedAt?.UtcTicks,
        UpdatedBy = role.UpdatedBy,
    };

    internal DynamicRole ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        IsDeleted = IsDeleted,
        DeletedAt = Instants.Of(DeletedAt),
        DeletedBy = DeletedBy,
        Name = Name,
        Description = Description,
        TenantId = TenantId,
        CreatedAt = Instants.Of(CreatedAt),
        CreatedBy = CreatedBy,
        UpdatedAt = Instants.Of(UpdatedAt),
        UpdatedBy = UpdatedBy,
    };
}
