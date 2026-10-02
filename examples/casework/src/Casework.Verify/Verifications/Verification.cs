using Casework.Verify.Enums;
using Casework.Verify.Events;
using Pragmatic.Events;
using Pragmatic.MultiTenancy;

namespace Casework.Verify.Entities;

/// <summary>
///     A verification somebody asked for, and — eventually — what came back.
/// </summary>
/// <remarks>
///     <para>
///         It has a life of its own, and that is the point of this service existing: the request creates
///         it <c>Pending</c>, an answer moves it to <c>Answered</c>, and nothing about that is visible to
///         Intake until a message says so. <c>VerificationAnswered</c> sends the outcome back; a
///         deadline that passes with nobody answering is Intake's to notice.
///     </para>
///     <para>
///         <c>CaseId</c> is a plain identifier and not a relation: the case lives in another service's
///         database, and a foreign key across a process boundary is the mistake this example exists to
///         not make. What keeps the two sides in agreement is the event, not the schema.
///     </para>
///     <para>
///         ⚠️ It is an <c>ITenantEntity</c> and the tenant is <b>not</b> in the message: the transport
///         carries it in a header and the consumer restores it into the consume scope, so the tenant
///         interceptor stamps this row without the handler doing anything. Which also means the row is
///         readable afterwards by this service's own API — whose filter is fail-closed — only because
///         that happened. <c>AVerificationIsAskedFor</c> asserts the column, because "it worked" and
///         "the tenant arrived" are otherwise indistinguishable while there is one organisation.
///     </para>
///     <para>
///         <c>DomainEventSource</c> because <see cref="Answer" /> raises <c>VerificationAnswered</c>, and
///         this service has an outbox of its own: the answer and the message announcing it
///         are one transaction here, exactly as the request was on Intake's side. The two services are
///         symmetric and neither knows the other's schema.
///     </para>
/// </remarks>
[Entity]
[Audited]
[ConcurrencyAware]
[StateMachine<VerificationStatus>]
public partial class Verification : DomainEventSource, IEntity, ITenantEntity
{
    public string TenantId { get; set; } = "";

    /// <summary>The case this was asked for, in Intake's database. Not a relation, on purpose.</summary>
    public Guid CaseId { get; private set; }

    /// <summary>What is being verified, as the request named it.</summary>
    [Required]
    [MaxLength(60)]
    public string Kind { get; private set; } = "";

    /// <summary>Where this verification is. Moved by the machine and by nothing else.</summary>
    public VerificationStatus Status { get; private set; } = VerificationStatus.Pending;

    /// <summary>
    ///     By when the asking service expects an answer.
    /// </summary>
    /// <remarks>
    ///     Recorded because it arrived in the request, and read by whoever works this service's queue.
    ///     ⚠️ Nothing here enforces it: a verification past its deadline is an ordinary <c>Pending</c>
    ///     row, and what notices is Intake's recurring deadline sweep, on the case.
    /// </remarks>
    public DateTimeOffset Deadline { get; private set; }

    /// <summary>What the verification found, once it has been answered.</summary>
    public VerificationOutcome? Outcome { get; private set; }

    /// <summary>Why, in the words of whoever answered.</summary>
    [MaxLength(500)]
    public string? Note { get; private set; }

    /// <summary>When the answer was recorded: this service's clock.</summary>
    public DateTimeOffset? AnsweredOn { get; private set; }

    /// <summary>
    ///     Who answered — the system or the person, as the caller's token named them.
    /// </summary>
    /// <remarks>
    ///     This service's half of the schema change <c>MigratingEveryDatabaseAtOnce</c> asserts:
    ///     migrating N databases is only worth asserting if the two services move <b>independently</b>,
    ///     each to its own new schema. It is also the thing an auditor asks first about a verification.
    /// </remarks>
    [MaxLength(160)]
    public string? AnsweredBy { get; private set; }

    /// <summary>
    ///     Records an answer: the outcome, the note, and the move.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The refusal of a second answer is the state machine's — there is no move from
    ///         <c>Answered</c> — and not an <c>if</c> written here or in the operation. Which is also what
    ///         makes the endpoint safe to call twice by mistake: the second call is refused by the rule
    ///         rather than quietly overwriting what the first one found. And because the transition is
    ///         checked before the event is raised, a refused second call publishes <b>nothing</b>.
    ///     </para>
    ///     <para>
    ///         The event is raised here and not in the operation, which is what puts it in the same
    ///         transaction as the answer: the outbox interceptor reads it off this entity while EF is
    ///         saving. An operation that published on the bus itself would announce an answer that a
    ///         failed commit never recorded.
    ///     </para>
    /// </remarks>
    internal VoidResult<IError> Answer(
        VerificationOutcome outcome, string? note, DateTimeOffset now, string? answeredBy)
    {
        var moved = TransitionTo(VerificationStatus.Answered);
        if (moved.IsFailure)
            return moved;

        SetOutcome(outcome);
        SetNote(note);
        SetAnsweredOn(now);
        SetAnsweredBy(answeredBy);
        RaiseEvent(new VerificationAnswered(Id, CaseId, outcome, now));

        return moved;
    }

}
