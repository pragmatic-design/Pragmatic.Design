using Casework.Intake.Enums;
using Casework.Verify.Events;

namespace Casework.Intake.Dtos;

/// <summary>
///     A case as an operator's screen shows it.
/// </summary>
/// <remarks>
///     The tenant is not on it: a caller only ever sees their own, so putting the id in the answer would
///     publish a fact that is never a question. The number is, because it is what people quote.
/// </remarks>
[MapFrom<Case>]
[GenerateProjection]
public partial class CaseDto
{
    public Guid Id { get; init; }

    public string Number { get; init; } = "";

    public string Subject { get; init; } = "";

    public string Applicant { get; init; } = "";

    public CaseStatus Status { get; init; }

    public DateTimeOffset? VerificationAskedOn { get; init; }

    /// <summary>
    ///     When an answer is due, while one is awaited.
    /// </summary>
    /// <remarks>
    ///     On the wire because an operator has to be able to see what a case is waiting for and until
    ///     when — a deadline nobody can read is a timer, and this one is a fact of the domain. It disappears when the case stops waiting, which the serializer does for
    ///     free: a null is not written at all.
    /// </remarks>
    public DateTimeOffset? VerificationDueOn { get; init; }

    /// <summary>What the verification found, once its answer has arrived from the other service.</summary>
    public VerificationOutcome? VerificationOutcome { get; init; }

    public DateTimeOffset? VerificationAnsweredOn { get; init; }
}
