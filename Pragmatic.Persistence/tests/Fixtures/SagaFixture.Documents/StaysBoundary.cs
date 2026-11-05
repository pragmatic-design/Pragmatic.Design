using Pragmatic.Actions.Attributes;
using Pragmatic.Messaging.Attributes;

namespace PragmaticSagas.Tests;

/// <summary>The stays, on the documents engine: <see cref="StaySaga" /> is kept in its database, and a step's messages go to its outbox.</summary>
[Boundary]
[EnableSagaPersistence]
[EnableOutbox]
public sealed partial class StaysBoundary;
