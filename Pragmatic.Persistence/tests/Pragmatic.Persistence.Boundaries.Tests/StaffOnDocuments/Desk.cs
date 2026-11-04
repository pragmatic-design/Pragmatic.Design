using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests.StaffOnDocuments;

/// <summary>A desk of the staff, on the documents engine: the boundary's own entity, so the boundary has a schema to hold the package's collections.</summary>
[Entity]
[BelongsTo<StaffOnDocumentsBoundary>]
public partial class Desk : IEntity
{
    public string Name { get; set; } = "";
}
