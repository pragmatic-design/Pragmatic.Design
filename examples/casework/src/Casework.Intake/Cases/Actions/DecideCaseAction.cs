using Casework.Verify.Events;

namespace Casework.Intake.Cases.Actions;

/// <summary>
///     Brings a case to an end: approved or rejected, from what the verification found.
/// </summary>
/// <remarks>
///     <para>
///         No <c>[Endpoint]</c>, and this one matters more than the others: deciding a case is the
///         <b>process's</b> business (<c>CaseProcessSaga</c>) and not a caller's. A route here would be a way
///         to approve a case nobody verified — refused by the state machine, yes, but offered by the API,
///         which is a different thing.
///     </para>
///     <para>
///         It is the saga's step that calls it, through the boundary's internal interface, so the case's
///         move and the saga's own state are written in the same request — and with <c>[EnableOutbox]</c>
///         on this boundary, the domain events of both are captured into the same transaction.
///     </para>
/// </remarks>
[DomainAction]
[LoadEntity<Case>(nameof(CaseId), FieldName = "_case")]
public partial class DecideCaseAction : DomainAction<CaseDto, IError>
{
    public required Guid CaseId { get; init; }

    public required VerificationOutcome Outcome { get; init; }

    /// <summary>When it was decided: the application's clock, and the moment the event carries.</summary>
    [FromClock]
    public DateTimeOffset DecidedOn { get; private set; }

    public override Task<Result<CaseDto, IError>> Execute(CancellationToken ct = default)
    {
        var decided = _case.Decide(Outcome, DecidedOn);

        if (decided.IsFailure)
            return Task.FromResult(Result<CaseDto, IError>.Failure(decided.Error));

        return Task.FromResult<Result<CaseDto, IError>>(CaseDto.FromEntity(_case));
    }
}
