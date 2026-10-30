using Pragmatic.Actions.Attributes;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The orders, on the Sql engine: its events are published to the transport through its Messaging outbox.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Sql)]
[EnableOutbox]
public sealed partial class OrdersBoundary;
