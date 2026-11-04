using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>
///     A <see cref="UserGroupAssignment" /> on the Sql engine: EF Core's table for the entity (<c>UserGroupAssignments</c>), column for
///     column.
/// </summary>
[Entity]
[Table("UserGroupAssignments")]
public sealed class UserGroupAssignmentRow
{
    [Key]
    public Guid PersistenceId { get; set; }

    public string UserId { get; set; } = "";

    public string GroupName { get; set; } = "";

    public string? TenantId { get; set; }

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
