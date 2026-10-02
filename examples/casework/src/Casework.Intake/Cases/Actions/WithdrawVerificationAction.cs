namespace Casework.Intake.Cases.Actions;

/// <summary>
///     Undoes a verification request the process gave up on: the case stops waiting.
/// </summary>
/// <remarks>
///     <para>
///         No <c>[Endpoint]</c>, for the same reason as <see cref="DecideCaseAction" />: withdrawing is
///         the <b>process's</b> business. A route here would let a caller cancel a verification somebody
///         else is carrying out, which is a decision the saga makes on an answer it cannot act on.
///     </para>
///     <para>
///         The saga's compensator reaches it through the boundary's internal interface, so the case's
///         move goes through the same operation pipeline as every other change to it.
///     </para>
/// </remarks>
[DomainAction]
[LoadEntity<Case>(nameof(CaseId), FieldName = "_case")]
public partial class WithdrawVerificationAction : DomainAction<CaseDto, IError>
{
    public required Guid CaseId { get; init; }

    public override Task<Result<CaseDto, IError>> Execute(CancellationToken ct = default)
    {
        var withdrawn = _case.WithdrawVerification();

        if (withdrawn.IsFailure)
            return Task.FromResult(Result<CaseDto, IError>.Failure(withdrawn.Error));

        return Task.FromResult<Result<CaseDto, IError>>(CaseDto.FromEntity(_case));
    }
}
