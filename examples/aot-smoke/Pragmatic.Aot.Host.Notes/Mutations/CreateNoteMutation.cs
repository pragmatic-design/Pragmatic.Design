using Pragmatic.Aot.Host.Notes.Dtos;
using Pragmatic.Aot.Host.Notes.Entities;

namespace Pragmatic.Aot.Host.Notes.Mutations;

/// <summary>Creates a note: the write the smoke makes over HTTP, through the Sql engine.</summary>
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(NotesPermissions.Note.Create)]
[Endpoint(HttpVerb.Post, "api/notes")]
[ReturnsDto<NoteDto>]
public partial class CreateNoteMutation : Mutation<Note>
{
    public required string Title { get; init; }

    public int Pages { get; init; }
}
