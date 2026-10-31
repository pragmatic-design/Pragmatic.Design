namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     An engine's types a boundary's registration names: its session and filters, the bridge's filters, unit of work and save phase, the
///     repository class nested in each entity; the methods of Pragmatic.Events.Engines, Pragmatic.Messaging.Engines and Pragmatic.Audit.Engines
///     that register a boundary's event outbox, Messaging outbox and audited entities on it (qualified); and the registration file's artifact and
///     generator name, so each engine's file says whose it is.
/// </summary>
internal sealed record EngineRegistrationModel(string Session, string Filters, string BridgeFilters, string UnitOfWork, string Phase, string Repository, string EventOutbox,
    string MessagingOutbox, string AuditedEntities, string Artifact, string Generator);
