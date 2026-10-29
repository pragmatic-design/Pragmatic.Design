using Pragmatic.Actions.Attributes;
using Pragmatic.Events.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The catalog, on the Sql engine: its events go out through its outbox.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Sql)]
[EnableEventOutbox]
public sealed partial class CatalogBoundary;
