using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The catalog, on the Sql engine.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Sql)]
public sealed partial class CatalogBoundary;
