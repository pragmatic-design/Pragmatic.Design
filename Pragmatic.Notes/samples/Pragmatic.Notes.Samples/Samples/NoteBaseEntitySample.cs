using Pragmatic.Notes;

namespace Pragmatic.Notes.Samples.Samples;

/// <summary>
/// Demonstrates defining a concrete note entity by deriving from <see cref="NoteBase{TEntityId}"/>,
/// and exercises its real runtime surface: <c>Id</c> / <c>PersistenceId</c> (kept in sync), the
/// content + author fields, the audit/edit fields, and the soft-delete fields.
/// </summary>
/// <remarks>
/// <see cref="NoteBase{TEntityId}"/> supplies Id, PersistenceId, ParentEntityId, Content, AuthorId,
/// AuthorName, IsEdited, CreatedAt, UpdatedAt, UpdatedBy, IsDeleted, DeletedAt and DeletedBy.
/// </remarks>
public static class NoteBaseEntitySample
{
    /// <summary>A concrete note entity whose parent (e.g. a ticket) is keyed by <see cref="Guid"/>.</summary>
    private sealed class TicketNote : NoteBase<Guid>;

    public static void Run()
    {
        Console.WriteLine("=== NoteBase concrete entity ===");

        var ticketId = Guid.NewGuid();

        // Create a note. Id is the note PK (Guid); PersistenceId mirrors Id (IEntity contract).
        var note = new TicketNote
        {
            Id = Guid.NewGuid(),
            ParentEntityId = ticketId,
            Content = "Customer requested a callback before shipping.",
            AuthorId = "user-42",
            AuthorName = "Alice (Support)",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Console.WriteLine("  After creation:");
        PrintNote(note);

        // Id <-> PersistenceId are the same backing value (PersistenceId is a passthrough to Id).
        note.PersistenceId = Guid.NewGuid();
        Console.WriteLine($"\n  PersistenceId is a passthrough to Id -> Id == PersistenceId: {note.Id == note.PersistenceId}");

        // Edit the note: update content and stamp the edit/audit fields.
        note.Content = "Customer requested a callback before shipping. (Confirmed by phone.)";
        note.IsEdited = true;
        note.UpdatedAt = note.CreatedAt.AddMinutes(8);
        note.UpdatedBy = "user-77";

        Console.WriteLine("\n  After an edit:");
        PrintNote(note);

        // Soft-delete via the ISoftDelete fields.
        note.IsDeleted = true;
        note.DeletedAt = note.CreatedAt.AddMinutes(30);
        note.DeletedBy = "user-77";

        Console.WriteLine("\n  After soft-delete:");
        PrintNote(note);
    }

    private static void PrintNote(TicketNote note)
    {
        Console.WriteLine($"    Id             = {note.Id}");
        Console.WriteLine($"    PersistenceId  = {note.PersistenceId}");
        Console.WriteLine($"    ParentEntityId = {note.ParentEntityId}");
        Console.WriteLine($"    Content        = \"{note.Content}\"");
        Console.WriteLine($"    AuthorId       = {note.AuthorId}");
        Console.WriteLine($"    AuthorName     = {note.AuthorName}");
        Console.WriteLine($"    IsEdited       = {note.IsEdited}");
        Console.WriteLine($"    CreatedAt      = {note.CreatedAt:u}");
        Console.WriteLine($"    UpdatedAt      = {(note.UpdatedAt is { } u ? u.ToString("u") : "<null>")}");
        Console.WriteLine($"    UpdatedBy      = {note.UpdatedBy ?? "<null>"}");
        Console.WriteLine($"    IsDeleted      = {note.IsDeleted}");
        Console.WriteLine($"    DeletedAt      = {(note.DeletedAt is { } d ? d.ToString("u") : "<null>")}");
        Console.WriteLine($"    DeletedBy      = {note.DeletedBy ?? "<null>"}");
    }
}
