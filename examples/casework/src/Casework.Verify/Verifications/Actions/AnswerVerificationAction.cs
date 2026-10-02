using Casework.Verify.Enums;
using Pragmatic.Identity;

namespace Casework.Verify.Verifications.Actions;

/// <summary>
///     Records what a verification found: the outcome, the note, and the move to <c>Answered</c>.
/// </summary>
/// <remarks>
///     <para>
///         This is the whole of this service's own API, and the caller is a <b>system</b>: an operator's
///         console or whatever performs the check. Verify holds no accounts — the token it accepts is a
///         service's, which is why its host module does not carry <c>[AnonymousHost]</c>. "No users"
///         and "no callers" are different claims, and only the first one is true here.
///     </para>
///     <para>
///         It publishes nothing itself. Intake learns the outcome from a message the verification raises
///         and this service's outbox delivers after the commit — so a case is still
///         <c>InVerification</c> when this returns, which
///         <c>AVerificationIsAskedFor.VerifyRecordsAnAnswerThroughItsOwnApi</c> asserts on purpose.
///     </para>
///     <para>
///         The second answer is refused by the state machine — there is no move out of <c>Answered</c> —
///         and not by a check written here. Which is what makes a duplicated call a 422 that says the
///         rule's name instead of an overwrite of what the first answer found.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(VerifyPermissions.Verification.Update)]
[Endpoint(HttpVerb.Post, "api/verifications/{id}/answer")]
[TransitionsTo<VerificationStatus>(VerificationStatus.Answered, When = TransitionTiming.ByBody)]
[LoadEntity<Verification>(nameof(Id), FieldName = "_verification")]
public partial class AnswerVerificationAction : DomainAction<VerificationDto, IError>
{
    // Who is calling, from the token — not an input. A caller that could name the answerer could name
    // somebody else.
    private ICurrentUser _caller = null!;

    public required Guid Id { get; init; }

    /// <summary>What the verification found.</summary>
    public required VerificationOutcome Outcome { get; init; }

    /// <summary>Why — read by whoever looks at the case afterwards.</summary>
    [MaxLength(500)]
    public string? Note { get; init; }

    /// <summary>When the answer was recorded: this service's clock, never the caller's.</summary>
    [FromClock]
    public DateTimeOffset AnsweredOn { get; private set; }

    public override Task<Result<VerificationDto, IError>> Execute(CancellationToken ct = default)
    {
        var answered = _verification.Answer(Outcome, Note, AnsweredOn, _caller.Id);

        // The factory and not `return answered.Error;`: the implicit conversion from the error is
        // user-defined, and C# does not apply one whose source type is an interface.
        if (answered.IsFailure)
            return Task.FromResult(Result<VerificationDto, IError>.Failure(answered.Error));

        return Task.FromResult<Result<VerificationDto, IError>>(VerificationDto.FromEntity(_verification));
    }
}
