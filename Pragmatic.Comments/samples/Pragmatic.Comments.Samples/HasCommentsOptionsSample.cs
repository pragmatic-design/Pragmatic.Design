using System.Reflection;
using Pragmatic.Comments;

namespace Pragmatic.Comments.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 1 — [HasComments] attribute options.
//
// [HasComments] is the trait marker the SG reads off the decorated entity to
// emit the comment entity, actions and endpoints. Here we declare entities that
// opt in with different option sets and read the options back via reflection —
// exactly the option surface the generator consumes from the symbol model.
// (Declaring the attribute compiles; the generated feature only exists inside a
// real Pragmatic host.)
// ─────────────────────────────────────────────────────────────────────────────

// Free-form defaults: MaxLength=2000, AllowReplies=true, AllowEditing=true.
[HasComments]
internal sealed class Article;

// Moderated public comments: every new comment starts PendingApproval.
[HasComments(RequireApproval = true, MaxLength = 500)]
internal sealed class BlogPost;

// Locked-down: no replies and no editing at all — with AllowEditing = false the SG
// generates neither the Update action nor its PUT endpoint (e.g. a guestbook).
[HasComments(AllowReplies = false, AllowEditing = false)]
internal sealed class Guestbook;

// Support ticket: staff-only internal notes, a 30-minute edit window, custom sub-boundary.
// EditWindowMinutes is an int with -1 as the "no limit" sentinel, so it is a plain
// attribute literal — the same convention [HasNotes] uses.
[HasComments(SupportInternalNotes = true, EditWindowMinutes = 30, SubBoundary = "TicketDiscussion")]
internal sealed class SupportTicket;

internal static class HasCommentsOptionsSample
{
    public static void Run()
    {
        Console.WriteLine("== Sample 1: [HasComments] attribute options ==");
        Console.WriteLine();

        Describe<Article>("free-form (defaults)");
        Describe<BlogPost>("moderated (RequireApproval)");
        Describe<Guestbook>("locked-down (no editing)");
        Describe<SupportTicket>("internal notes + edit window + custom sub-boundary");

        Console.WriteLine();
    }

    private static void Describe<T>(string label)
    {
        var attr = typeof(T).GetCustomAttribute<HasCommentsAttribute>();
        if (attr is null)
        {
            Console.WriteLine($"  {typeof(T).Name,-14} : (no [HasComments])");
            return;
        }

        // Default sub-boundary is "{Type}Comments" when not overridden.
        var effectiveSubBoundary = attr.SubBoundary ?? $"{typeof(T).Name}Comments";
        var editWindow = attr switch
        {
            { AllowEditing: false } => "editing disabled (no Update action generated)",
            { EditWindowMinutes: -1 } => "no limit",
            { EditWindowMinutes: var m } => $"{m} min",
        };

        Console.WriteLine($"  {typeof(T).Name,-14} : {label}");
        Console.WriteLine(
            $"      MaxLength={attr.MaxLength}, AllowReplies={attr.AllowReplies}, " +
            $"AllowEditing={attr.AllowEditing} (window={editWindow})");
        Console.WriteLine(
            $"      RequireApproval={attr.RequireApproval}, SupportInternalNotes={attr.SupportInternalNotes}");
        Console.WriteLine($"      SubBoundary='{effectiveSubBoundary}'");
    }
}
