using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>A notice, on the Sql engine: the entity that gives <see cref="NoticeBoardBoundary" /> a database.</summary>
[Entity]
[BelongsTo<NoticeBoardBoundary>]
public partial class Notice : IEntity
{
    public string Title { get; set; } = "";
}
