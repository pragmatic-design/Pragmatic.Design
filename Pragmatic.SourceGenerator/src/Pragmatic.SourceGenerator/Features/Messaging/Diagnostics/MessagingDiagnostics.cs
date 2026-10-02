using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Messaging.Diagnostics;

/// <summary>
///     Diagnostic descriptors for Pragmatic.Messaging source generator (PRAG0800-0840).
/// </summary>
internal static class MessagingDiagnostics
{
    // PRAG0800: [MessageHandler] on non-IMessageHandler class
    public static readonly DiagnosticDescriptor HandlerMustImplementInterface = DiagnosticFactory.Error(
        "PRAG0800",
        "Message handler must implement IMessageHandler<T>",
        "Type '{0}' is decorated with [MessageHandler] but does not implement IMessageHandler<T>",
        "Add an IMessageHandler<T> interface implementation to the handler class.");

    // PRAG0801 (handler must be partial) is the companion analyzer's, which reports it on the declaration
    // (NotPartialDiagnosticDescriptors).

    // PRAG0802: [Retry] with invalid config
    public static readonly DiagnosticDescriptor InvalidRetryConfig = DiagnosticFactory.Error(
        "PRAG0802",
        "Invalid retry configuration",
        "[Retry] on '{0}' has MaxAttempts <= 0",
        "Set MaxAttempts to a positive integer.");

    // PRAG0803: [MessageMiddleware] on non-IMessageMiddleware class
    public static readonly DiagnosticDescriptor MiddlewareMustImplementInterface = DiagnosticFactory.Error(
        "PRAG0803",
        "Message middleware must implement IMessageMiddleware",
        "Type '{0}' is decorated with [MessageMiddleware] but does not implement IMessageMiddleware",
        "Add an IMessageMiddleware interface implementation to the middleware class.");

    // =========================================================================
    // Saga diagnostics (PRAG0810-0814)
    // =========================================================================

    // PRAG0810 ("inconsistent saga transition") and PRAG0812 ("unreachable saga states") are retired and
    // not reused: reliable reachability/consistency analysis needs the full runtime transition semantics
    // the static SagaModel does not capture, so it reports valid sagas whose transitions are decided in
    // handler bodies. PRAG0814 (missing [SagaStart]) and PRAG0811 (state with no handler) cover the
    // reliably-detectable saga-shape cases.

    // PRAG0811: Saga state without handler (info — terminal states are expected)
    public static readonly DiagnosticDescriptor SagaOrphanedState = DiagnosticFactory.Info(
        "PRAG0811",
        "Saga state has no handler",
        "Saga '{0}' state '{1}' has no handler method (no [InState({1})] found)",
        "Terminal states (Completed, Cancelled) don't need handlers. Add a handler for non-terminal states.");

    // PRAG0813: [Saga<T>] but T is not enum
    public static readonly DiagnosticDescriptor SagaStateNotEnum = DiagnosticFactory.Error(
        "PRAG0813",
        "Saga state must be an enum",
        "Type '{0}' uses [Saga<{1}>] but '{1}' is not an enum type",
        "Change the type parameter to a valid enum type.");

    // PRAG0814: Saga without [SagaStart]
    public static readonly DiagnosticDescriptor SagaMissingStart = DiagnosticFactory.Error(
        "PRAG0814",
        "Saga has no start handler",
        "Saga '{0}' has no method decorated with [SagaStart]",
        "Add [SagaStart] to exactly one handler method.");

    // =========================================================================
    // Routing diagnostics (PRAG0816-0822)
    // PRAG0816 is enforced at the host by Composition.Validation.EventGraphValidator, which sees every
    // referenced module and reports events raised via [Raises<T>] with no handler anywhere — it needs
    // cross-assembly visibility the per-assembly Messaging generator does not have.
    // PRAG0815 (cross-boundary without [DependsOn]), PRAG0817 (queue-name collision) and PRAG0818
    // (consume without source outbox) were reserved here but removed: all three need cross-assembly
    // analysis and belong to host-level validation, not this generator.
    // =========================================================================

    // PRAG0816: Event without any handlers
    public static readonly DiagnosticDescriptor EventWithoutConsumers = DiagnosticFactory.Warning(
        "PRAG0816",
        "Event type has no consumers",
        "Event type '{0}' is published but has no registered [MessageHandler] in any boundary",
        "Add a handler implementing IMessageHandler<T> or remove the unused event.");

    // PRAG0819: Multiple [PartitionKey] properties on the same message type
    public static readonly DiagnosticDescriptor MultiplePartitionKeys = DiagnosticFactory.Warning(
        "PRAG0819",
        "Multiple [PartitionKey] properties",
        "Message type '{0}' declares [PartitionKey] on multiple properties; '{1}' is used and the others are ignored",
        "Keep a single [PartitionKey] property per message type.");

    // PRAG0820: Saga event with no correlation
    public static readonly DiagnosticDescriptor SagaEventWithoutCorrelation = DiagnosticFactory.Error(
        "PRAG0820",
        "Saga event has no correlation",
        "Saga '{0}' consumes event '{1}' which neither implements ICorrelatedMessage nor declares a [CorrelationKey] property — the event cannot be routed to a saga instance",
        "Implement ICorrelatedMessage on the event or mark a property with [CorrelationKey].");

    // PRAG0821: Multiple [CorrelationKey] properties on the same event
    public static readonly DiagnosticDescriptor MultipleCorrelationKeys = DiagnosticFactory.Warning(
        "PRAG0821",
        "Multiple [CorrelationKey] properties",
        "Saga event '{0}' declares [CorrelationKey] on multiple properties; the first (ordinal) is used and the others are ignored",
        "Keep a single [CorrelationKey] property per event type.");

    // =========================================================================
    // Outbox diagnostics (PRAG0831)
    // =========================================================================

    // PRAG0831: [EnableOutbox] on a boundary without Messaging.EFCore reference. Boundary-level
    // (mirrors PRAG0832 for [EnableSagaPersistence]). [EnableOutbox] targets a boundary, not a
    // DbContext, so PRAG0830 ("must be a DbContext") is retired and not reused.
    public static readonly DiagnosticDescriptor EnableOutboxWithoutEFCore = DiagnosticFactory.Warning(
        "PRAG0831",
        "[EnableOutbox] requires Pragmatic.Messaging.EFCore",
        "Boundary '{0}' is marked [EnableOutbox] but this project does not reference Pragmatic.Messaging.EFCore, so the outbox table is not mapped and no delivery pump is registered (the attribute is a no-op). Reference Pragmatic.Messaging.EFCore.",
        "Add a reference to Pragmatic.Messaging.EFCore to the boundary project so the generated DbContext can map __OutboxMessages and register the delivery pump.");

    // PRAG0832: [EnableSagaPersistence] on a boundary without Messaging.EFCore reference
    public static readonly DiagnosticDescriptor EnableSagaPersistenceWithoutEFCore = DiagnosticFactory.Warning(
        "PRAG0832",
        "[EnableSagaPersistence] requires Pragmatic.Messaging.EFCore",
        "Boundary '{0}' is marked [EnableSagaPersistence] but this project does not reference Pragmatic.Messaging.EFCore, so the saga tables are not mapped (the attribute is a no-op). Reference Pragmatic.Messaging.EFCore.",
        "Add a reference to Pragmatic.Messaging.EFCore to the boundary project so the generated DbContext can map __SagaInstances/__SagaSteps.");

    // PRAG0833: a boundary marked with BOTH [EnableOutbox] and [EnableEventOutbox]. Both interceptors
    // capture and clear the same IHasDomainEvents domain events on SaveChanges — whichever runs first
    // wins and the other silently sees nothing. A boundary must pick one delivery mechanism.
    public static readonly DiagnosticDescriptor ConflictingOutboxAttributes = DiagnosticFactory.Warning(
        "PRAG0833",
        "Conflicting outbox attributes on one boundary",
        "Boundary '{0}' is marked with both [EnableOutbox] (transport-publish) and [EnableEventOutbox] (in-process dispatch); both capture and clear the same domain events, so one silently wins. Keep exactly one.",
        "Remove either [EnableOutbox] or [EnableEventOutbox] from the boundary.");

    // PRAG0834: more than one boundary carries [EnableBatchProgress]. Batch progress is a SINGLE store
    // (unlike per-boundary saga/outbox), so exactly one boundary may own the __BatchProgress table.
    public static readonly DiagnosticDescriptor MultipleBatchProgressBoundaries = DiagnosticFactory.Warning(
        "PRAG0834",
        "[EnableBatchProgress] must mark exactly one boundary",
        "Boundary '{0}' is marked [EnableBatchProgress], but batch progress is a single store and another boundary already owns it; only one boundary may host the __BatchProgress table.",
        "Keep [EnableBatchProgress] on exactly one boundary — the one whose DbContext should host __BatchProgress.");

    // PRAG0835: [EnableBatchProgress] on a boundary without Pragmatic.Messaging.Batch reference
    // (the package that holds IBatchProgressStore, EfCoreBatchProgressStore and the EF config).
    public static readonly DiagnosticDescriptor EnableBatchProgressWithoutBatch = DiagnosticFactory.Warning(
        "PRAG0835",
        "[EnableBatchProgress] requires Pragmatic.Messaging.Batch",
        "Boundary '{0}' is marked [EnableBatchProgress] but this project does not reference Pragmatic.Messaging.Batch, so the batch-progress table is not mapped and no EF store is registered (the attribute is a no-op). Reference Pragmatic.Messaging.Batch.",
        "Add a reference to Pragmatic.Messaging.Batch so the generated DbContext can map __BatchProgress and register EfCoreBatchProgressStore.");

    // PRAG0836: [PublicEvent] on a type that is not a domain event.
    //
    // ⚠️ The attribute marks an event as public; it does not make one. An integration event is a domain
    // event that is also published — IIntegrationEvent extends IDomainEvent — and every consumer of the
    // marker starts from IDomainEvent: AsyncApiFeature catalogues types implementing it, and the
    // writer-side outbox captures events raised by tracked IHasDomainEvents entities. So on a plain
    // record the attribute is accepted, documented and inert: no document, no outbox row, no word said.
    // Measured on a contracts assembly whose record was marked [PublicEvent] and published to RabbitMQ —
    // its compilation emitted no AsyncAPI document at all.
    public static readonly DiagnosticDescriptor PublicEventOnNonDomainEvent = DiagnosticFactory.Warning(
        "PRAG0836",
        "[PublicEvent] marks a domain event, and this type is not one",
        "Type '{0}' is marked [PublicEvent] but does not implement IDomainEvent, so nothing reads the marker: it stays out of the generated AsyncAPI document and out of the transactional outbox. Implement IIntegrationEvent (which is IDomainEvent plus 'published') or IDomainEvent.",
        "An integration event is a domain event that is also published. Implement IIntegrationEvent on the type, or IDomainEvent and keep [PublicEvent] as the marker that publishes it.");

    // PRAG0837: an [EventHandler] on a boundary that carries [EnableOutbox] is registered and never run.
    //
    // ⚠️ Two halves that are individually right and together silent. OutboxInterceptor calls
    // ClearDomainEvents() while SavingChanges is in flight — it has to, or one event would be both an
    // outbox row and an in-process dispatch — and EfCoreUnitOfWork takes the events AFTER the commit,
    // which is also deliberate: an event announcing a write that failed is worse than one never sent.
    // By the time it takes them there are none, so every IDomainEventHandler<T> of that boundary stays
    // registered and is never entered: no log, no dead letter, nothing.
    //
    // The dangerous path is the upgrade: an application with working [EventHandler]s that adds
    // [EnableOutbox] — because its integration events should be transactional — loses every in-process
    // handler at that moment, with a green build and a green suite.
    public static readonly DiagnosticDescriptor EventHandlerOnOutboxBoundary = DiagnosticFactory.Warning(
        "PRAG0837",
        "An [EventHandler] of an [EnableOutbox] boundary never runs",
        "'{0}' handles a domain event of boundary '{1}', which is marked [EnableOutbox]: the outbox interceptor takes the entity's events during the save, so the unit of work finds none to dispatch afterwards and this handler is registered and never entered. Write it as [MessageHandler] IMessageHandler<T> — what arrives on such a boundary is the message published from the outbox row.",
        "On a boundary with [EnableOutbox] the events leave as messages. Handle them with [MessageHandler] IMessageHandler<T>, or take [EnableOutbox] off the boundary to keep in-process dispatch.");
}
