namespace Casework.Intake.Cases.Mutations;

/// <summary>
///     Corrects what a case says: its subject and who is asking.
/// </summary>
/// <remarks>
///     <para>
///         It does not touch the status. A case moves through <c>CaseStatus</c>'s declared transitions and
///         nowhere else, so there is no route by which a caller can approve a case — the absence is the
///         rule, and <c>TheCasesSurface</c> asserts it.
///     </para>
///     <para>
///         The invariant on the entity is what refuses a subject nobody can act on, whichever operation
///         wrote it: the same rule holds for the create above without being repeated here.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[Endpoint(HttpVerb.Put, "api/cases/{id}")]
[RequirePermission(IntakePermissions.Case.Update)]
[ReturnsDto<CaseDto>]
public partial class UpdateCaseMutation : Mutation<Case>
{
    [Required]
    [MaxLength(200)]
    public required string Subject { get; init; }

    [Required]
    [MaxLength(160)]
    public required string Applicant { get; init; }
}
