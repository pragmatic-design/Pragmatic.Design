using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.Attachments;

/// <summary>
///     Demonstrates <c>[PolymorphicAttachment]</c> + <c>[Attachable&lt;T&gt;]</c> on
///     <see cref="CommentNote"/>: a single comment table whose rows belong to different owner
///     types via the generated <c>OwnerType</c> / <c>OwnerId</c> columns, queried through the
///     generated <c>ForTicket()</c> / <c>ForArticle()</c> / <c>ForOwner&lt;T&gt;()</c> filters
///     (all emitted by the SG in <c>CommentNoteAttachmentExtensions</c>).
/// </summary>
public static class PolymorphicAttachmentSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ Polymorphic Attachment ([PolymorphicAttachment] + [Attachable<T>]) ═══");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<AttachmentsDbContext>()
            .UseInMemoryDatabase($"Attachments_{Guid.NewGuid():N}")
            .Options;

        await using var db = new AttachmentsDbContext(options);

        var ticket = new Ticket { Title = "Login fails on Safari" };
        var article = new Article { Headline = "Release notes v2" };
        db.Tickets.Add(ticket);
        db.Articles.Add(article);

        // The same CommentNote type attaches to either owner via the generated OwnerType/OwnerId.
        db.Comments.AddRange(
            Comment("Repro confirmed on 17.4", owner: ticket),
            Comment("Workaround: clear cache", owner: ticket),
            Comment("Typo in the second paragraph", owner: article));
        await db.SaveChangesAsync();

        // Generated query extensions from [Attachable<Ticket>] / [Attachable<Article>].
        var ticketComments = await db.Comments.ForTicket().ToListAsync();
        var articleComments = await db.Comments.ForArticle().ToListAsync();
        var viaGeneric = await db.Comments.ForOwner<Ticket>().ToListAsync();

        Console.WriteLine($"  Total comments in table : {await db.Comments.CountAsync()}");
        Console.WriteLine($"  ForTicket()             : {ticketComments.Count} (expects 2)");
        Console.WriteLine($"  ForArticle()            : {articleComments.Count} (expects 1)");
        Console.WriteLine($"  ForOwner<Ticket>()      : {viaGeneric.Count} (expects 2 — same as ForTicket)");
        Console.WriteLine();
        Console.WriteLine("  Ticket comments:");
        foreach (var c in ticketComments)
            Console.WriteLine($"    [{c.OwnerType}:{c.OwnerId[..8]}…] {c.Text}");
        Console.WriteLine();
    }

    // OwnerType stores the FULL type name (typeof(T).FullName) to match the generated ForOwner<T>() /
    // Query{X}s discriminator, so same-named owners in different namespaces never collide.
    private static CommentNote Comment(string text, Ticket owner)
        => new() { Text = text, OwnerType = typeof(Ticket).FullName!, OwnerId = owner.Id.ToString() };

    private static CommentNote Comment(string text, Article owner)
        => new() { Text = text, OwnerType = typeof(Article).FullName!, OwnerId = owner.Id.ToString() };
}
