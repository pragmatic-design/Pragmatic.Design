// ReSharper disable once CheckNamespace
namespace Pragmatic.Aot.Host.Notes;

/// <summary>The smoke's one boundary, persisted by the documents engine: the Sql smoke's boundary on the other engine.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Documents)]
public sealed partial class NotesBoundary;
