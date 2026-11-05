using Pragmatic.Actions.Attributes;
using Pragmatic.Notifications.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The notices, on the Sql engine: the application's notification records are kept in its database.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Sql)]
[StoresNotifications]
public sealed partial class NoticeBoardBoundary;
