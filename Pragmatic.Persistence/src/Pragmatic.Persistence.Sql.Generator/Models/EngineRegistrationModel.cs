namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>An engine's types a boundary's registration names: its session and filters, the bridge's filters, unit of work and save phase, the repository class nested in each entity.</summary>
internal sealed record EngineRegistrationModel(string Session, string Filters, string BridgeFilters, string UnitOfWork, string Phase, string Repository);
