using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>A bulletin, on the documents engine: the entity that gives <see cref="MailroomBoundary" /> a database.</summary>
[Entity]
[BelongsTo<MailroomBoundary>]
public partial class Bulletin : IEntity
{
    public string Title { get; set; } = "";
}
