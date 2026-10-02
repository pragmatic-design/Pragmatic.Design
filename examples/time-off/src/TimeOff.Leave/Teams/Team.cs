namespace TimeOff.Leave.Entities;

/// <summary>
///     A group of employees with one manager, who decides their leave requests.
/// </summary>
/// <remarks>
///     Deleting a team leaves its members without one, and an employee who manages a team cannot be
///     deleted. Both are said on the collection side, which is where a relationship declared on both
///     ends reads its delete behavior — and whose default, left unsaid, would cascade.
/// </remarks>
[Entity]
[Auditable]
[Relation.ManyToOne<Employee>.WithNavigation("Manager", Inverse = "ManagedTeams")]
[Relation.OneToMany<Employee>.WithNavigation("Members", Inverse = "Team", OnDelete = DeleteBehavior.SetNull)]
public partial class Team : IEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; private set; } = "";
}
