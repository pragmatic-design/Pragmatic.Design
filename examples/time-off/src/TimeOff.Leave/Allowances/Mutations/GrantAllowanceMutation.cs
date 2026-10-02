namespace TimeOff.Leave.Allowances.Mutations;

/// <summary>
///     HR grants an employee their allowance of a kind for a year. A second grant for the same employee,
///     kind and year is refused: it is corrected, not duplicated.
/// </summary>
/// <remarks>
///     The employee and the kind only have to exist — nothing here reads them — so they are checked with
///     <c>[RequireExists]</c>, an <c>EXISTS</c> each, and a key that names nothing is a 404 naming it rather than
///     the database's foreign-key violation. The keys are still written to the allowance, as its relations.
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(LeavePermissions.Allowance.Create)]
[Endpoint(HttpVerb.Post, "api/allowances")]
[CreatedAt("/api/allowances/{Id}")]
[ReturnsDto<AllowanceDto>]
[RequireExists<Employee>(nameof(EmployeeId))]
[RequireExists<AbsenceKind>(nameof(AbsenceKindId))]
public partial class GrantAllowanceMutation : Mutation<Allowance>
{
    public required Guid EmployeeId { get; init; }

    public required Guid AbsenceKindId { get; init; }

    public required int Year { get; init; }

    public required decimal Entitled { get; init; }

    public decimal CarriedOver { get; init; }
}
