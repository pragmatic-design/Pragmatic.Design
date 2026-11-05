using Pragmatic.Actions.Attributes;
using Pragmatic.Notifications.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The bulletins, on the documents engine: the application's notification records are kept in its database.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Documents)]
[StoresNotifications]
public sealed partial class MailroomBoundary;
