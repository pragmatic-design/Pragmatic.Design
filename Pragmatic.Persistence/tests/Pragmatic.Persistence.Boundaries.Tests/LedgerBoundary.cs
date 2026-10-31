using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The invoices, on the Sql engine: its <c>[Audited]</c> entity's writes append to the audit trail in its own database.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Sql)]
public sealed partial class LedgerBoundary;
