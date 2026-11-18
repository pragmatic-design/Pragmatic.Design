using Pragmatic.Aot.Host.Notes.Entities;
using Pragmatic.Aot.Host.Notes.Enums;

namespace Pragmatic.Aot.Host.Notes.Dtos;

/// <summary>A note on the wire.</summary>
[MapFrom<Note>]
[GenerateProjection]
public partial class NoteDto
{
    public Guid Id { get; init; }

    public string Title { get; init; } = "";

    public int Pages { get; init; }

    /// <summary>An enum on the wire: by its name, as the host's enum converter writes it, under Native AOT too (#857).</summary>
    public NoteKind Kind { get; init; }
}
