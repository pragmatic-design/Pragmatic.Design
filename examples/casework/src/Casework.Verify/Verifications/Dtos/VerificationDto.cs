using Casework.Verify.Enums;

namespace Casework.Verify.Dtos;

/// <summary>
///     A verification as this service's own API shows it.
/// </summary>
/// <remarks>
///     The tenant is not on it, for the same reason as Intake's case DTO: a caller only ever sees their
///     own organisation's rows, so putting the id in the answer would publish a fact that is never a
///     question.
/// </remarks>
[MapFrom<Verification>]
[GenerateProjection]
public partial class VerificationDto
{
    public Guid Id { get; init; }

    public Guid CaseId { get; init; }

    public string Kind { get; init; } = "";

    public VerificationStatus Status { get; init; }

    public DateTimeOffset Deadline { get; init; }

    public VerificationOutcome? Outcome { get; init; }

    public string? Note { get; init; }

    public DateTimeOffset? AnsweredOn { get; init; }
}
