using Pragmatic.Aot.Host.Notes.Entities;

namespace Pragmatic.Aot.Host.Notes.Dtos;

/// <summary>A note on the wire.</summary>
[MapFrom<Note>]
[GenerateProjection]
public partial class NoteDto
{
    public Guid Id { get; init; }

    public string Title { get; init; } = "";

    public int Pages { get; init; }
}
