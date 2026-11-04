using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>
///     A <see cref="UserRoleAssignment" /> on the Sql engine: EF Core's table for the entity (<c>UserRoleAssignments</c>), column for column.
/// </summary>
[Entity]
[Table("UserRoleAssignments")]
public sealed class UserRoleAssignmentRow
{
    [Key]
    public Guid PersistenceId { get; set; }

    public string UserId { get; set; } = "";

    public string RoleName { get; set; } = "";

    public string? TenantId { get; set; }

    public string? AssignedBy { get; set; }

    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }

    internal static UserRoleAssignmentRow From(UserRoleAssignment assignment) => new()
    {
        PersistenceId = assignment.PersistenceId,
        UserId = assignment.UserId,
        RoleName = assignment.RoleName,
        TenantId = assignment.TenantId,
        AssignedBy = assignment.AssignedBy,
        ValidFrom = assignment.ValidFrom,
        ValidTo = assignment.ValidTo,
    };

    internal UserRoleAssignment ToEntity() => new()
    {
        PersistenceId = PersistenceId,
        UserId = UserId,
        RoleName = RoleName,
        TenantId = TenantId,
        AssignedBy = AssignedBy,
        ValidFrom = ValidFrom,
        ValidTo = ValidTo,
    };
}
