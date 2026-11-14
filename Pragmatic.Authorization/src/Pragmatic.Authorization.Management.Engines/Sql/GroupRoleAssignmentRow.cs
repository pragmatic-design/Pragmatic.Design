using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>
///     A <see cref="GroupRoleAssignment" /> on the Sql engine: EF Core's table for the entity (<c>GroupRoleAssignments</c>), column for
///     column.
/// </summary>
[Entity]
[Table("GroupRoleAssignments")]
[Pragmatic.Persistence.Entity.Index(nameof(GroupName), nameof(RoleName), nameof(TenantId))] // the entity's [Index]
public sealed class GroupRoleAssignmentRow
{
    [Key]
    public Guid PersistenceId { get; set; }

    [MaxLength(256)]
    public string GroupName { get; set; } = "";

    [MaxLength(256)]
    public string RoleName { get; set; } = "";

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [MaxLength(256)]
    public string? AssignedBy { get; set; }

    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }

    internal static GroupRoleAssignmentRow From(GroupRoleAssignment assignment) => new()
    {
        PersistenceId = assignment.PersistenceId,
        GroupName = assignment.GroupName,
        RoleName = assignment.RoleName,
        TenantId = assignment.TenantId,
        AssignedBy = assignment.AssignedBy,
        ValidFrom = assignment.ValidFrom,
        ValidTo = assignment.ValidTo,
    };

    internal GroupRoleAssignment ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        GroupName = GroupName,
        RoleName = RoleName,
        TenantId = TenantId,
        AssignedBy = AssignedBy,
        ValidFrom = ValidFrom,
        ValidTo = ValidTo,
    };
}
