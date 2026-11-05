using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Authorization.Management.Engines.Documents;

/// <summary>
///     A <see cref="UserRoleAssignment" /> on the documents engine (collection <c>UserRoleAssignments</c>): its fields, its instants in UTC
///     ticks.
/// </summary>
[Document("UserRoleAssignments")]
public sealed partial class UserRoleAssignmentDocument
{
    /// <summary>The collection in the importing boundary's <c>{Boundary}DocSchema</c>.</summary>
    public static readonly DocCollectionSchema Schema = new("UserRoleAssignments", "/id",
        [new DocIndex("IX_UserRoleAssignments_UserId_RoleName_TenantId", ["UserId", "RoleName", "TenantId"])], []); // the entity's [Index] (#490)

    [Key]
    public Guid PersistenceId { get; set; }

    public string UserId { get; set; } = "";

    public string RoleName { get; set; } = "";

    public string? TenantId { get; set; }

    public string? AssignedBy { get; set; }

    public long ValidFrom { get; set; }

    public long? ValidTo { get; set; }

    internal static UserRoleAssignmentDocument From(UserRoleAssignment assignment) => new()
    {
        PersistenceId = assignment.PersistenceId,
        UserId = assignment.UserId,
        RoleName = assignment.RoleName,
        TenantId = assignment.TenantId,
        AssignedBy = assignment.AssignedBy,
        ValidFrom = assignment.ValidFrom.UtcTicks,
        ValidTo = assignment.ValidTo?.UtcTicks,
    };

    internal UserRoleAssignment ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        UserId = UserId,
        RoleName = RoleName,
        TenantId = TenantId,
        AssignedBy = AssignedBy,
        ValidFrom = Instants.Of(ValidFrom),
        ValidTo = Instants.Of(ValidTo),
    };
}
