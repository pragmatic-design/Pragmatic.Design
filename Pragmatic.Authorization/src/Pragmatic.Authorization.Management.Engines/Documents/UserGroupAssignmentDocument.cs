using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Authorization.Management.Engines.Documents;

/// <summary>
///     A <see cref="UserGroupAssignment" /> on the documents engine (collection <c>UserGroupAssignments</c>): its fields, its instants in UTC
///     ticks.
/// </summary>
[Document("UserGroupAssignments")]
public sealed partial class UserGroupAssignmentDocument
{
    /// <summary>The collection in the importing boundary's <c>{Boundary}DocSchema</c>.</summary>
    public static readonly DocCollectionSchema Schema = new("UserGroupAssignments", "/id", [], []);

    [Key]
    public Guid PersistenceId { get; set; }

    public string UserId { get; set; } = "";

    public string GroupName { get; set; } = "";

    public string? TenantId { get; set; }

    public string? AssignedBy { get; set; }

    public long ValidFrom { get; set; }

    public long? ValidTo { get; set; }

    internal UserGroupAssignment ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        UserId = UserId,
        GroupName = GroupName,
        TenantId = TenantId,
        AssignedBy = AssignedBy,
        ValidFrom = Instants.Of(ValidFrom),
        ValidTo = Instants.Of(ValidTo),
    };
}
