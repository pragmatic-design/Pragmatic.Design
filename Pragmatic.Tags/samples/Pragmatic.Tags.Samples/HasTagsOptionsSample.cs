using System.Reflection;
using Pragmatic.Tags;

namespace Pragmatic.Tags.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 1 — [HasTags] attribute options.
//
// [HasTags] is a TRAIT marker. In the finished feature a source generator reads
// these options off the decorated entity and emits the Tag entity, the junction
// today: the attribute itself and its option surface, read back via reflection
// exactly as the SG would read it from the symbol model.
// ─────────────────────────────────────────────────────────────────────────────

// Default free-form usage: AllowCustom=true, MaxPerEntity=50, CaseSensitive=false.
[HasTags]
internal sealed class Article;

// Curated taxonomy: users cannot invent tags, only pre-seeded ones are accepted.
[HasTags(AllowCustom = false)]
internal sealed class SupportTicket;

// Shared scope across multiple entity types — both feed the same "global" tag set.
[HasTags(Scope = "global")]
internal sealed class BlogPost;

[HasTags(Scope = "global")]
internal sealed class Comment;

// Case-sensitive tags: "API" and "api" are distinct.
[HasTags(CaseSensitive = true)]
internal sealed class CodeSnippet;

// Hard limit + sub-boundary override for the generated boundary interface.
[HasTags(MaxPerEntity = 5, SubBoundary = "PhotoLabels")]
internal sealed class Photo;

internal static class HasTagsOptionsSample
{
    public static void Run()
    {
        Console.WriteLine("== Sample 1: [HasTags] attribute options ==");
        Console.WriteLine();

        Describe<Article>("free-form (defaults)");
        Describe<SupportTicket>("curated taxonomy (AllowCustom=false)");
        Describe<BlogPost>("shared scope 'global'");
        Describe<Comment>("shared scope 'global'");
        Describe<CodeSnippet>("case-sensitive");
        Describe<Photo>("limited + sub-boundary override");

        Console.WriteLine();
        Console.WriteLine("  BlogPost and Comment share Scope='global' => the SG would");
        Console.WriteLine("  generate them against ONE shared tag set (cross-entity sharing).");
        Console.WriteLine();
    }

    private static void Describe<T>(string label)
    {
        var attr = typeof(T).GetCustomAttribute<HasTagsAttribute>();
        if (attr is null)
        {
            Console.WriteLine($"  {typeof(T).Name,-13} : (no [HasTags])");
            return;
        }

        // Default Scope is the parent type name when not explicitly set.
        var effectiveScope = attr.Scope ?? typeof(T).Name;
        // Default sub-boundary is "{Type}Tags" when not overridden.
        var effectiveSubBoundary = attr.SubBoundary ?? $"{typeof(T).Name}Tags";
        var limit = attr.MaxPerEntity == 0 ? "unlimited" : attr.MaxPerEntity.ToString();

        Console.WriteLine($"  {typeof(T).Name,-13} : {label}");
        Console.WriteLine(
            $"      MaxPerEntity={limit}, AllowCustom={attr.AllowCustom}, " +
            $"CaseSensitive={attr.CaseSensitive}");
        Console.WriteLine(
            $"      Scope='{effectiveScope}', SubBoundary='{effectiveSubBoundary}'");
    }
}
