using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The reviews, on the documents engine.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Documents)]
public sealed partial class ReviewsBoundary;
