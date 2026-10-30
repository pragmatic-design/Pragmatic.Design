using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A boundary's <c>Add{Boundary}Persistence</c>: its unit of work keyed by its type, its entities' repositories, on one engine; and its event
///     outbox when the boundary is <c>[EnableEventOutbox]</c> and Pragmatic.Events.Engines is referenced (<paramref name="EventOutbox" />); its
///     Messaging outbox when the boundary is <c>[EnableOutbox]</c> and Pragmatic.Messaging.Engines is referenced (<paramref name="MessagingOutbox" />);
///     the audit trail's entries when one of its entities is <c>[Audited]</c> and Pragmatic.Audit.Engines is referenced (<paramref name="AuditedEntities" />).
/// </summary>
internal sealed record BoundaryPersistenceModel(string Hint, string? Namespace, string BoundaryName, string BoundaryType, EngineRegistrationModel Engine, EquatableArray<string> EntityTypes,
    bool EventOutbox, bool MessagingOutbox, bool AuditedEntities);
