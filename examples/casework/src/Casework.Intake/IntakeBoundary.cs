using Pragmatic.Jobs.Attributes;
using Pragmatic.Messaging.Attributes;

namespace Casework.Intake;

/// <summary>
///     Intake: a case, what it needs verified, and what was decided.
/// </summary>
/// <remarks>
///     <para>
///         The boundary is where this service's declarations land: each of them is a
///         fact about this boundary's <b>database</b>, which is why they are attributes here and not
///         calls in a host.
///     </para>
///     <para>
///         <c>[EnableJobPersistence]</c> puts the durable job store's tables — <c>__Jobs</c> and
///         <c>__RecurringJobs</c> — in this database, so the deadline sweep survives a
///         restart and two hosts cannot both run the same occurrence. ⚠️ A deadline that lives in memory
///         is not a deadline.
///     </para>
///     <para>
///         <c>[EnableSagaPersistence]</c> maps <c>__SagaInstances</c> and <c>__SagaSteps</c> into this
///         boundary's DbContext: the case's process survives a restart because it is rows, not a
///         field in memory. ⚠️ There is no host call and no <c>.Saga.EFCore</c> package to add — the
///         attribute is the whole declaration, and it is <b>inert</b> without a reference to
///         <c>Pragmatic.Messaging.EFCore</c> (<c>PRAG0832</c>), which this project has for the outbox.
///     </para>
///     <para>
///         <c>[EnableOutbox]</c> is the <b>writer-side</b> one: it maps <c>__OutboxMessages</c> into this
///         boundary's DbContext, adds the capture interceptor and registers the delivery pump, so a
///         domain event raised by an entity is written in the same transaction as the row and published
///         to the transport afterwards. ⚠️ It is not <c>[EnableEventOutbox]</c>, which is Events' and
///         dispatches in process: one outbox per boundary, and both on the same one is <b>PRAG0833</b>,
///         because both capture and clear the same domain events. Invoicing uses the other one; this
///         service publishes to another process, so it needs this.
///     </para>
/// </remarks>
[Boundary]
[EnableOutbox]
[EnableSagaPersistence]
[EnableJobPersistence]
public partial class IntakeBoundary;
