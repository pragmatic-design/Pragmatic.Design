using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Authorization.Management.Engines.Documents;

/// <summary>A <see cref="DynamicGroup" /> on the documents engine (collection <c>DynamicGroups</c>): its fields, its instants in UTC ticks.</summary>
[Document("DynamicGroups")]
public sealed partial class DynamicGroupDocument
{
    /// <summary>The collection in the importing boundary's <c>{Boundary}DocSchema</c>, with the entity's unique index.</summary>
    public static readonly DocCollectionSchema Schema = new("DynamicGroups", "/id", [new DocIndex("IX_DynamicGroups_Name_TenantId", ["Name", "TenantId"], Unique: true)], []);

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

    internal static DynamicGroupDocument From(DynamicGroup group) => new()
    {
        PersistenceId = group.PersistenceId,
        IsDeleted = group.IsDeleted,
        DeletedAt = group.DeletedAt?.UtcTicks,
        DeletedBy = group.DeletedBy,
        Name = group.Name,
        Description = group.Description,
        TenantId = group.TenantId,
        CreatedAt = group.CreatedAt.UtcTicks,
        CreatedBy = group.CreatedBy,
        UpdatedAt = group.UpdatedAt?.UtcTicks,
        UpdatedBy = group.UpdatedBy,
    };

    internal DynamicGroup ToEntity() => new()
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
