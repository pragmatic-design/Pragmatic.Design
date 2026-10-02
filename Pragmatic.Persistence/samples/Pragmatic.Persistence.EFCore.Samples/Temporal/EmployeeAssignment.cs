using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Temporal;

/// <summary>
///     A time-bounded assignment of an employee to a <see cref="Department"/>.
///     <c>[TemporalRelation&lt;Department&gt;(MaxActive = 1)]</c> + <see cref="ITemporalRelation"/>
///     make the SG generate <c>EmployeeAssignmentTemporalExtensions</c>:
///     <c>Active()</c>, <c>ActiveAt(date)</c>, <c>IncludeHistory()</c>,
///     <c>ForDepartment(id)</c>, and <c>ActiveForDepartment(id)</c> — with validation scoped
///     per department (at most one active head per department at a time).
/// </summary>
[Entity]
[Relation.ManyToOne<Department>]
[TemporalRelation<Department>(MaxActive = 1)]
public partial class EmployeeAssignment : IEntity, ITemporalRelation
{
    public Guid PersistenceId { get; set; } = Guid.CreateVersion7();

    public Guid Id => PersistenceId;

    public Guid EmployeeId { get; set; }

    public string Role { get; set; } = "";

    // ITemporalRelation — null ValidTo means "currently active".
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
}
