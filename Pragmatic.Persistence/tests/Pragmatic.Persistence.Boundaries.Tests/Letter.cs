using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>A letter, on the documents engine: every write to it is recorded in the audit trail.</summary>
[Entity]
[Audited]
[BelongsTo<ArchiveBoundary>]
public partial class Letter : IEntity
{
    public string Subject { get; set; } = "";
}
