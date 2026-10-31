using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The front office, on the Sql engine: its bookings belong to a tenant.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Sql)]
public sealed partial class FrontOfficeBoundary;
