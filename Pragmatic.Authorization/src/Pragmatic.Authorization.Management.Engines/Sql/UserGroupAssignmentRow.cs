using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>
///     A <see cref="UserGroupAssignment" /> on the Sql engine: EF Core's table for the entity (<c>UserGroupAssignments</c>), column for
///     column.
/// </summary>
[Entity]
[Table("UserGroupAssignments")]
[Pragmatic.Persistence.Entity.Index(nameof(UserId), nameof(GroupName), nameof(TenantId))] // the entity's [Index] (#490)
public sealed class UserGroupAssignmentRow
{
    [Key]
    public Guid PersistenceId { get; set; }

    [MaxLength(256)]
    public string UserId { get; set; } = "";

    [MaxLength(256)]
    public string GroupName { get; set; } = "";

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [MaxLength(256)]
    public string? AssignedBy { get; set; }

    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }

    internal UserGroupAssignment ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        UserId = UserId,
        GroupName = GroupName,
        TenantId = TenantId,
        AssignedBy = AssignedBy,
        ValidFrom = ValidFrom,
        ValidTo = ValidTo,
    };
}
