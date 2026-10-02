using Casework.Intake.Enums;
using Casework.Intake.Events;
using Casework.Verify.Events;
using Pragmatic.Events;
using Pragmatic.MultiTenancy;

namespace Casework.Intake.Entities;

/// <summary>
///     A case: somebody asking for something that has to be verified before it can be decided.
/// </summary>
/// <remarks>
///     <para>
///         It is a tenant's row (<c>ITenantEntity</c>) on the shared schema and in a database of its own
///         alike: retro-fitting a tenant column onto rows that already exist is a migration nobody needs
///         to write if the column is always there.
///     </para>
///     <para>
///         <c>DomainEventSource</c> is what makes the outbox possible at all: the writer-side outbox
///         captures the domain events of <b>tracked entities</b> (<c>OutboxInterceptor</c> reads
///         <c>IHasDomainEvents</c> during <c>SavingChanges</c>), so an event published any other way —
///         <c>IMessageBus.PublishAsync</c> from an operation, say — leaves no row and gets no
///         transactional guarantee. That is the rule in practice: a cross-service contract is the
///         publisher's domain event.
///     </para>
///     <para>
///         The number is generated and is what people quote on the telephone; the row's identity is the
///         key the framework keeps. A case is never addressed by its number in a route — the two are
///         different things on purpose.
///     </para>
/// </remarks>
[Entity]
[Audited]
[ConcurrencyAware]
[StateMachine<CaseStatus>]
// The documents cascade: a document has no life without its case, and nothing addresses one without
// naming the case. Declared on this side because the collection navigation is generated from the
// attribute that names it — `Inverse` on the child's [Relation.ManyToOne] names the other end for the
// schema, it does not generate this property.
[Relation.OneToMany<CaseDocument>.WithNavigation("Documents", Inverse = "Case", OnDelete = DeleteBehavior.Cascade)]
public partial class Case : DomainEventSource, IEntity, ITenantEntity
{
    public string TenantId { get; set; } = "";

    /// <summary>What people quote when they ring up about it: <c>CASE-00001</c>, per tenant.</summary>
    [LogicKey]
    [GeneratedValue("CASE-{SEQ:5}")]
    [MaxLength(20)]
    public string Number { get; private set; } = "";

    /// <summary>What the applicant asked for, in their words.</summary>
    [Required]
    [MaxLength(200)]
    public string Subject { get; private set; } = "";

    /// <summary>Who is asking.</summary>
    [Required]
    [MaxLength(160)]
    public string Applicant { get; private set; } = "";

    /// <summary>
    ///     Where to write to the applicant, when there is somewhere.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A case the office has to answer in writing needs an address, and the decision is sent to
    ///         it. It is also the schema change <c>MigratingEveryDatabaseAtOnce</c> carries across N
    ///         databases — a <b>real</b> one rather than a column called <c>Migrated</c>.
    ///     </para>
    ///     <para>
    ///         Nullable, and that is what makes it a migration an existing database can survive: a
    ///         <c>NOT NULL</c> column added to a table with rows in it is a breaking change, and the
    ///         diff engine blocks those unless the application asks for <c>Force()</c>. What a schema
    ///         change costs is decided here, not by the migration run.
    ///     </para>
    /// </remarks>
    [MaxLength(200)]
    public string? ApplicantEmail { get; private set; }

    /// <summary>
    ///     The language this applicant reads, frozen onto the case when it is opened.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Frozen, and that is the whole point.</b> The decision letter may be rendered days
    ///         later by a job that has no request, or by an operator whose own language is different, so
    ///         "the applicant's language" cannot be read from whoever happens to be asking. It is a fact
    ///         about this case, written down when the case is opened — the same shape Invoicing froze
    ///         onto the invoice.
    ///     </para>
    ///     <para>
    ///         Null means the application's default: an applicant who did not say is not an applicant
    ///         who reads nothing.
    ///     </para>
    /// </remarks>
    [MaxLength(20)]
    public string? ApplicantLanguage { get; private set; }

    /// <summary>Where the case is. Moved by the machine and by nothing else.</summary>
    public CaseStatus Status { get; private set; } = CaseStatus.Open;

    /// <summary>When a verification was asked for, if one was.</summary>
    public DateTimeOffset? VerificationAskedOn { get; private set; }

    /// <summary>
    ///     When an answer is due, if one is awaited.
    /// </summary>
    /// <remarks>
    ///     ⚠️ On the <b>case</b>, and that is a decision: the deadline is a fact of the
    ///     domain — somebody agreed to it and an operator can read it — and not a timer somewhere. The
    ///     same value travels in the request so the other service can show what it is late against, and
    ///     this column is what the recurring job reads.
    /// </remarks>
    public DateTimeOffset? VerificationDueOn { get; private set; }

    /// <summary>What the verification found, once its answer has arrived.</summary>
    public VerificationOutcome? VerificationOutcome { get; private set; }

    /// <summary>When the answer was recorded here — this service's clock, not the answering one's.</summary>
    public DateTimeOffset? VerificationAnsweredOn { get; private set; }

    /// <summary>
    ///     The <c>EventId</c> of the answer already applied to this case.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This column <b>is</b> the idempotency of the consumer, and it is a column and not a
    ///     deduplication window on purpose: delivery is at-least-once, the framework's
    ///     <c>EnableIdempotency()</c> store is in memory and keyed on the <em>message</em> id, and a
    ///     window is a period of time rather than a guarantee. The case knows which answer it has
    ///     already recorded, so a redelivery — or a second publish of the same event, which is what
    ///     <c>AnOutcomeThatComesBackTwice</c> does — changes nothing.
    /// </remarks>
    public Guid? AnsweredByEvent { get; private set; }

    /// <summary>
    ///     A case is about something: a subject of a few characters is a case nobody can act on.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An <c>[Invariant]</c> and not a validation attribute, because it is a rule about the case
    ///         rather than about an input: it holds however the row is written, and the invoker checks it
    ///         on both write paths.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>internal</c> and not <c>private</c>, and that is load-bearing: the generated invoker
    ///         is another class, so it can only call a rule that is at least <c>internal</c> — a
    ///         <c>private</c> one is left out of the generated check and the rule never fires. Measured
    ///         here: a nine-character subject came back <c>201</c> with the rule written right where it
    ///         is now. The generator says nothing about it.
    ///     </para>
    ///     <para>
    ///         The key is a full error key — the localizer reads <c>{key}.title</c> and
    ///         <c>{key}.detail</c> — so it carries the <c>error.</c> prefix the translation files use.
    ///         The sentence in the attribute is what a host with no translation for it answers.
    ///     </para>
    /// </remarks>
    [Invariant("A case needs a subject of at least ten characters",
        MessageKey = "error.case.subject.too.short")]
    internal bool SubjectIsSubstantial() => Subject.Trim().Length >= 10;

    internal static Case Opened(string subject, string applicant)
    {
        var @case = Create();
        @case.SetSubject(subject);
        @case.SetApplicant(applicant);

        return @case;
    }

    /// <summary>
    ///     Asks for a verification of this case: the row moves and the event is raised together.
    /// </summary>
    /// <remarks>
    ///     The event is raised here and not in the operation, which is what puts it in the same
    ///     transaction as the change: the interceptor reads it off this entity while EF is saving. An
    ///     operation that published it on the bus instead would announce a change that a failed commit
    ///     never made.
    /// </remarks>
    /// <summary>
    ///     Records a document that has already been stored: what it is called, how long it is, what it
    ///     hashes to and where its bytes are.
    /// </summary>
    /// <remarks>
    ///     The case is what adds it, so nothing outside can put a document on a case it does not own,
    ///     and the row is written in the case's own transaction. The bytes went to the store first: a
    ///     failed commit therefore leaves a file nobody references, which is the cheaper of the two
    ///     mistakes — the other is a row pointing at bytes that were never written.
    /// </remarks>
    internal CaseDocument Attach(
        string fileName, string contentType, long length, string sha256, string storageKey, DateTimeOffset now)
    {
        var document = CaseDocument.Stored(fileName, contentType, length, sha256, storageKey, now);
        Documents.Add(document);

        return document;
    }

    internal VoidResult<IError> AskForVerification(string kind, DateTimeOffset now)
    {
        var moved = TransitionTo(CaseStatus.InVerification);
        if (moved.IsFailure)
            return moved;

        var dueOn = now.Add(VerificationWindow);

        SetVerificationAskedOn(now);
        SetVerificationDueOn(dueOn);
        RaiseEvent(new VerificationRequested(Id, kind, dueOn, now));

        return moved;
    }

    /// <summary>
    ///     Nobody answered by the deadline: the case stops waiting.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Called by the recurring deadline sweep and by nothing else. It clears the deadline, so a
    ///         case cannot be expired twice and the job's next run does not find it again — and so that
    ///         asking for a verification again (<c>Expired → InVerification</c>) starts a new window
    ///         rather than inheriting the one that ran out.
    ///     </para>
    ///     <para>
    ///         ⚠️ It does not clear <c>VerificationAskedOn</c> or the outcome: what happened, happened.
    ///         An expiry is the process giving up on an answer, not the case forgetting it asked.
    ///     </para>
    /// </remarks>
    internal VoidResult<IError> ExpireVerification()
    {
        var moved = TransitionTo(CaseStatus.Expired);
        if (moved.IsFailure)
            return moved;

        SetVerificationDueOn(null);

        return moved;
    }

    /// <summary>
    ///     The process gave up on the verification it asked for: the case stops waiting for it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The saga's compensation. Not the same thing as expiring: an expiry is nobody answering by
    ///         the deadline, this is an answer that verified nothing — and the case goes back to
    ///         <c>Open</c> rather than to <c>Expired</c>, because there is nothing to wait for any more
    ///         and an operator can ask again straight away.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>It raises nothing, and the other service is not told.</b> That is the honest record:
    ///         the process gives up because an answer
    ///         arrived that it could not act on, so Verify has <b>already answered</b> — and an answer is
    ///         a fact it recorded, not an effect to undo. A message saying "withdraw it" would ask that
    ///         service to unsay what it found because this one could not use it, and the only state it
    ///         could move to would have to be reachable from <c>Answered</c>, which is the rule that
    ///         makes a second answer impossible. The compensation is this side's: the case stops waiting.
    ///     </para>
    ///     <para>
    ///         ⚠️ Like <see cref="ExpireVerification" /> it does not clear <c>VerificationAskedOn</c>:
    ///         what happened, happened. Giving up is the process's, not the case forgetting it asked.
    ///     </para>
    /// </remarks>
    internal VoidResult<IError> WithdrawVerification()
    {
        var moved = TransitionTo(CaseStatus.Open);
        if (moved.IsFailure)
            return moved;

        SetVerificationDueOn(null);

        return moved;
    }

    /// <summary>
    ///     Records the answer a verification came back with, once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Idempotent, and by <b>construction</b> rather than by configuration: the case remembers the
    ///         event it has already applied, so the same answer arriving twice is written once. Returns
    ///         <see langword="false" /> for a repeat, which is what lets the handler say "already done"
    ///         instead of pretending it wrote something.
    ///     </para>
    ///     <para>
    ///         ⚠️ It does not move the status. Deciding the case is the saga's business and a
    ///         rejection compensates; an answer is a <b>fact</b> that arrived, and writing it
    ///         down is all this does. A handler that also decided would be a second place where a case's
    ///         life is written.
    ///     </para>
    /// </remarks>
    internal bool RecordVerificationOutcome(Guid eventId, VerificationOutcome outcome, DateTimeOffset now)
    {
        if (AnsweredByEvent == eventId)
            return false;

        SetVerificationOutcome(outcome);
        SetVerificationAnsweredOn(now);
        SetAnsweredByEvent(eventId);

        return true;
    }

    /// <summary>
    ///     Decides the case: approved or rejected, according to what the verification found.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Which move it is comes from the outcome, and <b>whether</b> it is allowed comes from the
    ///         state machine: there is no transition to <c>Approved</c> from anywhere but
    ///         <c>InVerification</c>, so a case nobody asked a verification for cannot be decided, and a
    ///         case decided twice is refused the second time. Neither rule is written here.
    ///     </para>
    ///     <para>
    ///         Called by the saga and by nothing else: deciding is the process's job, which is
    ///         why there is no route to it — <c>TheCasesSurface.NoRouteWritesAStatus</c> keeps that true.
    ///     </para>
    ///     <para>
    ///         It raises <c>CaseDecided</c>, which is what the applicant's mail hangs on. The
    ///         moment comes from the caller and not from a clock read here: an entity that read the time
    ///         would be a second source of "now" in a transaction that already has one.
    ///     </para>
    /// </remarks>
    internal VoidResult<IError> Decide(VerificationOutcome outcome, DateTimeOffset now)
    {
        var moved = TransitionTo(outcome == Casework.Verify.Events.VerificationOutcome.Passed
            ? CaseStatus.Approved
            : CaseStatus.Rejected);

        if (moved.IsFailure)
            return moved;

        // Raised here and not in the operation, which is what puts it in the same transaction as the
        // move: a decision that was announced and not committed would be a letter about a case that is
        // still waiting. What the announcement is *for* — telling the applicant — is a handler that
        // runs after the commit (TellTheApplicantTheDecision).
        RaiseEvent(new CaseDecided(Id, Number, outcome, now));

        return moved;
    }

    /// <summary>
    ///     How long the other service has to answer before the case is late.
    /// </summary>
    /// <remarks>
    ///     One place, with a name, and on the side that <b>asks</b>: the deadline is Intake's expectation
    ///     and travels in the request, so Verify does not have to know this number and cannot disagree
    ///     with it. The recurring deadline sweep is what makes a passed deadline an event; this is only
    ///     how long the window is.
    /// </remarks>
    internal static readonly TimeSpan VerificationWindow = TimeSpan.FromDays(10);
}
