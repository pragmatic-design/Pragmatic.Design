using Pragmatic.Tags;

namespace Pragmatic.Tags.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 2 — Concrete TagBase / EntityTagBase<TEntityId> entities.
//
// In the finished feature the SG emits one "{Boundary}Tag : TagBase" plus a
// junction "{Parent}Tag : EntityTagBase<TEntityId>" per entity. Since the SG is
// types that exist today: the Id <-> PersistenceId forwarding, the content /
// scope / usage fields on TagBase, and the composite (ParentEntityId, TagId) key
// on EntityTagBase<TEntityId>.
// ─────────────────────────────────────────────────────────────────────────────

// The shared Tag entity (one per boundary). Mirrors what the SG would generate.
internal sealed class ArticleTag : TagBase;

// The M:N junction between Article (Guid PK) and ArticleTag.
internal sealed class ArticleTagLink : EntityTagBase<Guid>;

internal static class TagEntitiesSample
{
    public static void Run()
    {
        Console.WriteLine("== Sample 2: TagBase / EntityTagBase<TEntityId> entities ==");
        Console.WriteLine();

        // --- TagBase ---------------------------------------------------------
        var tag = new ArticleTag
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Value = "urgent",          // normalized (lower-case) canonical form
            DisplayValue = "Urgent",   // original casing preserved for display
            Scope = "Article",
            UsageCount = 3,
            CreatedAt = DateTimeOffset.UnixEpoch,
            CreatedBy = "alice",
        };

        Console.WriteLine("  TagBase (ArticleTag):");
        Console.WriteLine($"      Id            = {tag.Id}");
        // PersistenceId forwards to Id — the IEntity contract.
        Console.WriteLine($"      PersistenceId = {tag.PersistenceId}   (forwards to Id)");
        Console.WriteLine($"      Value         = '{tag.Value}'  DisplayValue = '{tag.DisplayValue}'");
        Console.WriteLine($"      Scope         = '{tag.Scope}'  UsageCount = {tag.UsageCount}");
        Console.WriteLine($"      CreatedAt     = {tag.CreatedAt:u}  CreatedBy = {tag.CreatedBy}");

        // Setting PersistenceId writes straight through to Id.
        var rerouted = Guid.Parse("22222222-2222-2222-2222-222222222222");
        tag.PersistenceId = rerouted;
        Console.WriteLine($"      after PersistenceId = {rerouted:D}: Id == {tag.Id}  -> {tag.Id == rerouted}");
        Console.WriteLine();

        // --- EntityTagBase<TEntityId> ---------------------------------------
        var articleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var link = new ArticleTagLink
        {
            ParentEntityId = articleId,        // FK to the Article (Guid PK)
            TagId = tag.Id,                    // FK to the ArticleTag
            AddedAt = DateTimeOffset.UnixEpoch,
            AddedBy = "alice",
        };

        Console.WriteLine("  EntityTagBase<Guid> (ArticleTagLink — junction):");
        Console.WriteLine($"      Composite key = (ParentEntityId={link.ParentEntityId}, TagId={link.TagId})");
        Console.WriteLine($"      AddedAt       = {link.AddedAt:u}  AddedBy = {link.AddedBy}");
        Console.WriteLine();
    }
}
