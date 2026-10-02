using Pragmatic.Comments;
using Pragmatic.Result;

namespace Pragmatic.Comments.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 3 — ICommentPolicy<TEntityId> default behavior + a custom policy.
//
// ICommentPolicy is a real, usable runtime hook today (independent of the SG).
// It uses C# default interface methods, so the simplest policy implements
// nothing and inherits "always allow / no-op". The SG-generated Add/Delete/Edit
// actions call these hooks when a policy is registered in DI. This sample shows
// the inherited DIM behavior, a custom policy that rejects via
// CommentRejectedError, and the normalize/allow/hook sequence an action drives.
// ─────────────────────────────────────────────────────────────────────────────

// Default policy: overrides NOTHING — every member is the interface default.
internal sealed class DefaultArticlePolicy : ICommentPolicy<Guid>;

// Custom policy for an Article (Guid-keyed): block-list on add, ban after delete.
internal sealed class ModeratedArticlePolicy : ICommentPolicy<Guid>
{
    private static readonly string[] BannedWords = ["spam", "scam"];

    public int Added { get; private set; }

    public Task<VoidResult<CommentRejectedError>> CanAddAsync(
        Guid parentId, string content, string? authorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Task.FromResult(VoidResult<CommentRejectedError>.Failure(
                CommentRejectedError.Because("Comment content cannot be empty.")));

        foreach (var word in BannedWords)
        {
            if (content.Contains(word, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(VoidResult<CommentRejectedError>.Failure(
                    CommentRejectedError.Because($"Comment contains a banned word: '{word}'.")));
        }

        return Task.FromResult(VoidResult<CommentRejectedError>.Success());
    }

    public Task OnAddedAsync(Guid parentId, Guid commentId, CancellationToken ct = default)
    {
        Added++;
        return Task.CompletedTask;
    }

    // Only the author or staff may delete; demo rejects an unknown requester.
    public Task<VoidResult<CommentRejectedError>> CanDeleteAsync(
        Guid commentId, string? requesterId, CancellationToken ct = default)
        => Task.FromResult(requesterId is null
            ? VoidResult<CommentRejectedError>.Failure(
                CommentRejectedError.Because("Anonymous callers cannot delete comments."))
            : VoidResult<CommentRejectedError>.Success());
}

internal static class CommentPolicySample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("== Sample 3: ICommentPolicy<Guid> ==");
        Console.WriteLine();

        var articleId = Guid.NewGuid();

        // --- Default interface-method behavior -------------------------------
        ICommentPolicy<Guid> def = new DefaultArticlePolicy();
        var defAdd = await def.CanAddAsync(articleId, "anything goes", "user-1");
        var defDel = await def.CanDeleteAsync(Guid.NewGuid(), null);
        Console.WriteLine("  Default policy (inherited DIM behavior):");
        Console.WriteLine($"      CanAdd(...)    = {Describe(defAdd)}  (default allows everything)");
        Console.WriteLine($"      CanDelete(...) = {Describe(defDel)}");
        Console.WriteLine();

        // --- Custom policy ---------------------------------------------------
        var custom = new ModeratedArticlePolicy();
        Console.WriteLine("  Custom policy (block-list + delete guard + hooks):");

        await TryAdd(custom, articleId, "Great post, thanks!");
        await TryAdd(custom, articleId, "Buy cheap SPAM here");
        await TryAdd(custom, articleId, "   ");

        var del = await custom.CanDeleteAsync(Guid.NewGuid(), requesterId: null);
        Console.WriteLine($"      delete by anonymous -> {Describe(del)}");

        Console.WriteLine($"      lifecycle counter   -> Added={custom.Added}");
        Console.WriteLine();
    }

    // Mirrors the can-add -> persist -> on-added sequence the SG action performs.
    private static async Task TryAdd(ModeratedArticlePolicy policy, Guid articleId, string content)
    {
        var canAdd = await policy.CanAddAsync(articleId, content, "user-1");
        if (canAdd.IsFailure)
        {
            Console.WriteLine($"      add '{Trim(content)}' -> rejected: {canAdd.Error.Reason}");
            return;
        }

        await policy.OnAddedAsync(articleId, Guid.NewGuid());
        Console.WriteLine($"      add '{Trim(content)}' -> stored");
    }

    private static string Describe(VoidResult<CommentRejectedError> r)
        => r.Match(() => "Ok", e => $"Rejected ({e.Reason})");

    private static string Trim(string s)
        => string.IsNullOrWhiteSpace(s) ? "(blank)" : (s.Length > 24 ? s[..24] + "…" : s);
}
