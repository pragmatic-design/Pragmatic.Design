using Pragmatic.Aot.Host.Notes.Dtos;
using Pragmatic.Aot.Host.Notes.Entities;

namespace Pragmatic.Aot.Host.Notes.Queries;

/// <summary>Reads one note by id: the read the smoke makes back over HTTP.</summary>
[Query<Note, NoteDto>(Single = true)]
[Endpoint(HttpVerb.Get, "api/notes/{id}")]
[RequirePermission(NotesPermissions.Note.Read)]
public partial class GetNoteQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
