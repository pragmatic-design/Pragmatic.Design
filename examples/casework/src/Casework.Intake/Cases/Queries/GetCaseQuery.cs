namespace Casework.Intake.Cases.Queries;

/// <summary>
///     One case, by id — this tenant's, because the filter is not optional.
/// </summary>
/// <remarks>
///     It is also what makes the <c>201</c> of <c>OpenCaseMutation</c> honest: a <c>[CreatedAt]</c>
///     pointing at a route nobody serves answers 405 to whoever follows it.
/// </remarks>
[Query<Case, CaseDto>(Single = true)]
[Endpoint(HttpVerb.Get, "api/cases/{id}")]
[RequirePermission(IntakePermissions.Case.Read)]
public partial class GetCaseQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
