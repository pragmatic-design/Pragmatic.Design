using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Authorization.Management.Engines.Documents;

/// <summary>
///     A <see cref="GroupRoleAssignment" /> on the documents engine (collection <c>GroupRoleAssignments</c>): its fields, its instants in UTC
///     ticks.
/// </summary>
[Document("GroupRoleAssignments")]
public sealed partial class GroupRoleAssignmentDocument
{
    /// <summary>The collection in the importing boundary's <c>{Boundary}DocSchema</c>.</summary>
    public static readonly DocCollectionSchema Schema = new("GroupRoleAssignments", "/id",
        [new DocIndex("IX_GroupRoleAssignments_GroupName_RoleName_TenantId", ["GroupName", "RoleName", "TenantId"])], []); // the entity's [Index]

    [Key]
    public Guid PersistenceId { get; set; }

    public string GroupName { get; set; } = "";

    public string RoleName { get; set; } = "";

    public string? TenantId { get; set; }

    public string? AssignedBy { get; set; }

    public long ValidFrom { get; set; }

    public long? ValidTo { get; set; }

    internal static GroupRoleAssignmentDocument From(GroupRoleAssignment assignment) => new()
    {
        PersistenceId = assignment.PersistenceId,
        GroupName = assignment.GroupName,
        RoleName = assignment.RoleName,
        TenantId = assignment.TenantId,
        AssignedBy = assignment.AssignedBy,
        ValidFrom = assignment.ValidFrom.UtcTicks,
        ValidTo = assignment.ValidTo?.UtcTicks,
    };

    internal GroupRoleAssignment ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        GroupName = GroupName,
        RoleName = RoleName,
        TenantId = TenantId,
        AssignedBy = AssignedBy,
        ValidFrom = Instants.Of(ValidFrom),
        ValidTo = Instants.Of(ValidTo),
    };
}
