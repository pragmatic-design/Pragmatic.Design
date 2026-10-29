using Pragmatic.Actions.Attributes;
using Pragmatic.Events.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The reviews, on the documents engine: its events go out through its outbox.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Documents)]
[EnableEventOutbox]
public sealed partial class ReviewsBoundary;
