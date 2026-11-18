namespace Pragmatic.Aot.Host.Notes;

/// <summary>The smoke's one boundary, persisted by the Sql engine.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Sql)]
public sealed partial class NotesBoundary;
