using Pragmatic.Tags;

namespace Pragmatic.Tags.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 3 — ITagPolicy<TEntityId> default behavior + a custom implementation.
//
// ITagPolicy is a real, usable runtime hook today (independent of the SG). It
// uses C# default interface methods so the simplest policy is "implement nothing"
// and inherit Normalize (trim + lower) and IsAllowedAsync (reject blank). This
// sample shows both the inherited DIM behavior and a custom policy that
// overrides Normalize (slug-style) and IsAllowedAsync (block-list) and observes
// the OnTagAdded/OnTagRemoved lifecycle hooks.
// ─────────────────────────────────────────────────────────────────────────────

// Default policy: overrides NOTHING — every member is the interface default.
internal sealed class DefaultArticlePolicy : ITagPolicy<Guid>;

// Custom policy for an Article (Guid-keyed): slug normalization, block-list,
// and lifecycle hooks that count add/remove events.
internal sealed class SluggedArticlePolicy : ITagPolicy<Guid>
{
    private static readonly HashSet<string> Blocked =
        new(StringComparer.OrdinalIgnoreCase) { "spam", "nsfw" };

    public int Added { get; private set; }
    public int Removed { get; private set; }

    public string Normalize(string tagValue)
        => tagValue.Trim().ToLowerInvariant().Replace(' ', '-');

    public Task<bool> IsAllowedAsync(string tagValue, CancellationToken ct = default)
        => Task.FromResult(
            !string.IsNullOrWhiteSpace(tagValue) && !Blocked.Contains(tagValue));

    public Task OnTagAddedAsync(Guid entityId, Guid tagId, string tagValue, CancellationToken ct = default)
    {
        Added++;
        return Task.CompletedTask;
    }

    public Task OnTagRemovedAsync(Guid entityId, Guid tagId, string tagValue, CancellationToken ct = default)
    {
        Removed++;
        return Task.CompletedTask;
    }
}

internal static class TagPolicySample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("== Sample 3: ITagPolicy<TEntityId> ==");
        Console.WriteLine();

        // --- Default interface-method behavior -------------------------------
        ITagPolicy<Guid> def = new DefaultArticlePolicy();
        Console.WriteLine("  Default policy (inherited DIM behavior):");
        Console.WriteLine($"      Normalize('  Hello World ') = '{def.Normalize("  Hello World ")}'");
        Console.WriteLine($"      IsAllowed('hello')         = {await def.IsAllowedAsync("hello")}");
        Console.WriteLine($"      IsAllowed('   ')           = {await def.IsAllowedAsync("   ")}  (blank rejected)");
        Console.WriteLine();

        // --- Custom policy ---------------------------------------------------
        var custom = new SluggedArticlePolicy();
        Console.WriteLine("  Custom policy (slug normalize + block-list + hooks):");
        Console.WriteLine($"      Normalize('  Hello World ') = '{custom.Normalize("  Hello World ")}'  (spaces -> '-')");
        Console.WriteLine($"      IsAllowed('design')        = {await custom.IsAllowedAsync("design")}");
        Console.WriteLine($"      IsAllowed('spam')          = {await custom.IsAllowedAsync("spam")}  (block-listed)");
        Console.WriteLine();

        // Simulate the add/remove flow the SG-generated action would drive.
        var articleId = Guid.NewGuid();
        await ApplyTag(custom, articleId, "Hello World");
        await ApplyTag(custom, articleId, "C# Tips");
        await RemoveTag(custom, articleId, "hello-world");

        Console.WriteLine($"      lifecycle counters -> Added={custom.Added}, Removed={custom.Removed}");
        Console.WriteLine();
    }

    // Mirrors the normalize -> allow-check -> hook sequence an Add{Parent}Tag
    // action would perform once the SG exists.
    private static async Task ApplyTag(SluggedArticlePolicy policy, Guid articleId, string raw)
    {
        var normalized = policy.Normalize(raw);
        if (!await policy.IsAllowedAsync(normalized))
        {
            Console.WriteLine($"      add '{raw}' -> rejected");
            return;
        }

        await policy.OnTagAddedAsync(articleId, Guid.NewGuid(), normalized);
        Console.WriteLine($"      add '{raw}' -> stored as '{normalized}'");
    }

    private static async Task RemoveTag(SluggedArticlePolicy policy, Guid articleId, string normalized)
    {
        await policy.OnTagRemovedAsync(articleId, Guid.NewGuid(), normalized);
        Console.WriteLine($"      remove '{normalized}' -> ok");
    }
}
