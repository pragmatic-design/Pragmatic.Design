using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Temporal;

/// <summary>
///     Parent entity for the temporal-relation demo. An <see cref="EmployeeAssignment"/>
///     links an employee to a department for a validity window.
/// </summary>
[Entity]
public partial class Department : IEntity
{
    public Guid PersistenceId { get; set; } = Guid.CreateVersion7();

    public Guid Id => PersistenceId;

    public string Name { get; set; } = "";
}
