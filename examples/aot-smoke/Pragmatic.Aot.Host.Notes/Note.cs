using Pragmatic.Aot.Host.Notes.Enums;

namespace Pragmatic.Aot.Host.Notes.Entities;

/// <summary>A note: what the smoke writes over HTTP and reads back.</summary>
[Entity]
[BelongsTo<NotesBoundary>]
public partial class Note : IEntity
{
    [Required]
    public string Title { get; private set; } = "";

    public int Pages { get; private set; }

    public NoteKind Kind { get; private set; }
}
