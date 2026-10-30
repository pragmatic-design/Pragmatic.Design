using Pragmatic.Actions.Attributes;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The shipments, on the documents engine: its events are published to the transport through its Messaging outbox.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Documents)]
[EnableOutbox]
public sealed partial class ShipmentsBoundary;
