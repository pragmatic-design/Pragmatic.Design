using Casework.Verify.Events;

namespace Casework.Intake.Events;

/// <summary>
///     What the process asks for when the answer has arrived: decide this case, this way.
/// </summary>
/// <remarks>
///     <para>
///         A message and not a method call, because a saga cannot hold a dependency: a saga persisted
///         with EF Core is constructed by <c>EfCoreSagaRepository&lt;TSaga, TState&gt;</c>, which
///         constrains <c>TSaga</c> to <c>new()</c> — measured as <b>CS0310</b> the moment the saga took
///         <c>IIntakeInternalActions</c> in its constructor. A saga is persisted data plus decisions; its
///         effects leave through the bus, which the orchestrator publishes <b>after</b> the saga's state
///         is committed.
///     </para>
///     <para>
///         ⚠️ It is a command and not a domain event, and the difference is who may send it: this is
///         Intake asking itself to do something, it carries no <c>EventId</c> and nothing outside this
///         service has any business publishing it. It is <b>not</b> an <c>IIntegrationEvent</c> and it is
///         not in either contract project — a consumer that could send this could decide any case.
///     </para>
///     <para>
///         It lives in <c>Casework.Intake.Events</c> so its topic is <c>intake.events</c>, the same one
///         this service already publishes to: the router takes the second segment of the namespace, and a
///         <c>Commands</c> namespace would have opened a second topic for one message.
///     </para>
/// </remarks>
/// <param name="CaseId">The case to decide.</param>
/// <param name="Outcome">What the verification found — the decision follows from it.</param>
public sealed record DecideTheCase(Guid CaseId, VerificationOutcome Outcome);
