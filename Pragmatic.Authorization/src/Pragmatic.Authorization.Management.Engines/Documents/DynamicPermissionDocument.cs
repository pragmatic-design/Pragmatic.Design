using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Authorization.Management.Engines.Documents;

/// <summary>A <see cref="DynamicPermission" /> on the documents engine (collection <c>DynamicPermissions</c>): its fields, its instants in UTC ticks.</summary>
[Document("DynamicPermissions")]
public sealed partial class DynamicPermissionDocument
{
    /// <summary>The collection in the importing boundary's <c>{Boundary}DocSchema</c>.</summary>
    public static readonly DocCollectionSchema Schema = new("DynamicPermissions", "/id", [], []);

    [Key]
    public Guid PersistenceId { get; set; }

    public bool IsDeleted { get; set; }

    public long? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public string? Category { get; set; }

    public string? TenantId { get; set; }

    public long CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public long? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    internal static DynamicPermissionDocument From(DynamicPermission permission) => new()
    {
        PersistenceId = permission.PersistenceId,
        IsDeleted = permission.IsDeleted,
        DeletedAt = permission.DeletedAt?.UtcTicks,
        DeletedBy = permission.DeletedBy,
        Name = permission.Name,
        Description = permission.Description,
        Category = permission.Category,
        TenantId = permission.TenantId,
        CreatedAt = permission.CreatedAt.UtcTicks,
        CreatedBy = permission.CreatedBy,
        UpdatedAt = permission.UpdatedAt?.UtcTicks,
        UpdatedBy = permission.UpdatedBy,
    };

    internal DynamicPermission ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        IsDeleted = IsDeleted,
        DeletedAt = Instants.Of(DeletedAt),
        DeletedBy = DeletedBy,
        Name = Name,
        Description = Description,
        Category = Category,
        TenantId = TenantId,
        CreatedAt = Instants.Of(CreatedAt),
        CreatedBy = CreatedBy,
        UpdatedAt = Instants.Of(UpdatedAt),
        UpdatedBy = UpdatedBy,
    };
}
