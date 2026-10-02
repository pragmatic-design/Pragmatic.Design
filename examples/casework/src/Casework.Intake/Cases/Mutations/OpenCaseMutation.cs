namespace Casework.Intake.Cases.Mutations;

/// <summary>
///     Opens a case: what is being asked, and by whom.
/// </summary>
/// <remarks>
///     The number is not an input: <c>[GeneratedValue]</c> on the entity takes the next one for this
///     tenant, and a caller that could choose it could collide with one that exists.
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[Endpoint(HttpVerb.Post, "api/cases")]
[RequirePermission(IntakePermissions.Case.Create)]
[CreatedAt("/api/cases/{Id}")]
[ReturnsDto<CaseDto>]
public partial class OpenCaseMutation : Mutation<Case>
{
    [Required]
    [MaxLength(200)]
    public required string Subject { get; init; }

    [Required]
    [MaxLength(160)]
    public required string Applicant { get; init; }

    /// <summary>
    ///     Where to write to them, when there is somewhere. Optional: a case can arrive at a counter.
    /// </summary>
    /// <remarks>
    ///     The column's only writer — a schema change is only a change if something can write it — and
    ///     the address the decision is sent to.
    /// </remarks>
    [MaxLength(200)]
    public string? ApplicantEmail { get; init; }

    /// <summary>
    ///     What language to write to them in — <c>it-IT</c>, <c>en-US</c>. Optional.
    /// </summary>
    /// <remarks>
    ///     It is taken here, at the one moment the applicant is in front of somebody, and kept
    ///     on the case: the letter is written later, by whoever or whatever, and asking them then would
    ///     be asking the wrong person.
    /// </remarks>
    [MaxLength(20)]
    public string? ApplicantLanguage { get; init; }
}
