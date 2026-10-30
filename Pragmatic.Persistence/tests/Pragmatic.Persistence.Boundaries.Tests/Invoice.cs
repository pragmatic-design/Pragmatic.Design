using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>An invoice, on the Sql engine: every write to it is recorded in the audit trail.</summary>
[Entity]
[Audited]
[BelongsTo<LedgerBoundary>]
public partial class Invoice : IEntity
{
    public string Number { get; set; } = "";
}
