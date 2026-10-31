using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The letters, on the documents engine: its <c>[Audited]</c> entity's writes append to the audit trail in its own database.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Documents)]
public sealed partial class ArchiveBoundary;
