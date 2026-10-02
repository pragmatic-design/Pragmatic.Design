using Pragmatic.Comments;

namespace Pragmatic.Comments.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 2 — Concrete CommentBase<TEntityId> entity + the status/visibility enums.
//
// In a real host the SG emits "{Parent}Comment : CommentBase<TEntityId>" per
// decorated entity. Here we HAND-WRITE the same shape (the SG output is not
// present in a bare console app) to show the base type that exists today: the
// Id <-> PersistenceId forwarding, content/author/threading fields, the
// CommentStatus lifecycle and CommentVisibility (public vs internal note), plus
// the soft-delete columns.
// ─────────────────────────────────────────────────────────────────────────────

// Mirrors the SG-generated child entity for an Article (Guid PK).
internal sealed class ArticleComment : CommentBase<Guid>;

internal static class CommentEntitySample
{
    public static void Run()
    {
        Console.WriteLine("== Sample 2: CommentBase<Guid> + status / visibility ==");
        Console.WriteLine();

        var articleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        // A top-level public comment, pending moderation.
        var comment = new ArticleComment
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ParentEntityId = articleId,
            Content = "Great article — thanks!",
            AuthorId = "user-42",
            AuthorName = "Alice",
            Status = CommentStatus.PendingApproval,
            Visibility = CommentVisibility.Public,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };

        Console.WriteLine("  CommentBase<Guid> (ArticleComment):");
        Console.WriteLine($"      Id            = {comment.Id}");
        // PersistenceId forwards to Id — the IEntity contract.
        Console.WriteLine($"      PersistenceId = {comment.PersistenceId}   (forwards to Id)");
        Console.WriteLine($"      ParentEntityId= {comment.ParentEntityId}");
        Console.WriteLine($"      Content       = '{comment.Content}'  by {comment.AuthorName} ({comment.AuthorId})");
        Console.WriteLine($"      Status        = {comment.Status}  Visibility = {comment.Visibility}");

        // Setting PersistenceId writes straight through to Id.
        var rerouted = Guid.Parse("22222222-2222-2222-2222-222222222222");
        comment.PersistenceId = rerouted;
        Console.WriteLine($"      after PersistenceId = {rerouted:D}: Id == {comment.Id}  -> {comment.Id == rerouted}");
        Console.WriteLine();

        // A staff-only internal note that is also a threaded reply.
        var internalNote = new ArticleComment
        {
            Id = Guid.NewGuid(),
            ParentEntityId = articleId,
            Content = "Flagged for fact-check before approval.",
            AuthorId = "moderator-1",
            ReplyToId = comment.Id,                  // threaded reply to the comment above
            Status = CommentStatus.Hidden,
            Visibility = CommentVisibility.Internal, // public API filters these out
            CreatedAt = DateTimeOffset.UnixEpoch,
        };

        Console.WriteLine("  Internal note (threaded reply):");
        Console.WriteLine($"      ReplyToId     = {internalNote.ReplyToId}  (reply to the comment above)");
        Console.WriteLine($"      Status        = {internalNote.Status}  Visibility = {internalNote.Visibility}");

        // Edit + soft-delete lifecycle (the mutable columns the update/delete actions touch).
        internalNote.Content = "Fact-checked — clearing for approval.";
        internalNote.IsEdited = true;
        internalNote.UpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(5);
        internalNote.UpdatedBy = "moderator-1";
        Console.WriteLine($"      after edit    : IsEdited={internalNote.IsEdited}, UpdatedBy={internalNote.UpdatedBy}");

        internalNote.IsDeleted = true;
        internalNote.DeletedAt = DateTimeOffset.UnixEpoch.AddMinutes(10);
        internalNote.DeletedBy = "moderator-1";
        Console.WriteLine($"      after delete  : IsDeleted={internalNote.IsDeleted}, DeletedBy={internalNote.DeletedBy}");

        Console.WriteLine();
        Console.WriteLine("  CommentStatus values    : " + string.Join(", ", Enum.GetNames<CommentStatus>()));
        Console.WriteLine("  CommentVisibility values: " + string.Join(", ", Enum.GetNames<CommentVisibility>()));
        Console.WriteLine();
    }
}
