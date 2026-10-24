namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     An engine's types a boundary's registration names: its session and filters, the bridge's filters, unit of work and save phase, the
///     repository class nested in each entity; and the registration file's artifact and generator name, so each engine's file says whose it is.
/// </summary>
internal sealed record EngineRegistrationModel(string Session, string Filters, string BridgeFilters, string UnitOfWork, string Phase, string Repository, string Artifact, string Generator);
