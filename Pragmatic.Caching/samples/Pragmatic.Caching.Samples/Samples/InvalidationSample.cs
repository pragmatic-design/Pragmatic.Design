namespace Pragmatic.Caching.Samples.Samples;

/// <summary>
///     [InvalidatesCache] — event-driven cache invalidation with explicit tags,
///     convention-based tags, and key-level removal.
/// </summary>
public static class InvalidationSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("3. Cache Invalidation — Event-Driven Patterns");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  3.1 Explicit tags + specific key removal");
        Console.WriteLine("  -------------------------------------------");
        Console.WriteLine("    [InvalidatesCache(\"users\", \"tenant:{TenantId}\", Keys = [\"user:{UserId}\"])]");
        Console.WriteLine("    public partial class UserUpdated { UserId, TenantId }");
        Console.WriteLine();
        Console.WriteLine("    SG generates ICacheInvalidator.InvalidateAsync() that:");
        Console.WriteLine("      1. cache.InvalidateByTagAsync(\"users\")");
        Console.WriteLine("      2. cache.InvalidateByTagAsync(\"tenant:42\")");
        Console.WriteLine("      3. cache.RemoveAsync(\"user:123\")");
        Console.WriteLine();

        Console.WriteLine("  3.2 Convention-based — type name becomes tag");
        Console.WriteLine("  -----------------------------------------------");
        Console.WriteLine("    [InvalidatesCache]");
        Console.WriteLine("    public partial class ProductCreated { ... }");
        Console.WriteLine();
        Console.WriteLine("    Convention: \"ProductCreated\" → remove suffix → \"Product\" → plural → \"products\"");
        Console.WriteLine("    SG generates: cache.InvalidateByTagAsync(\"products\")");
        Console.WriteLine();

        Console.WriteLine("  3.3 Multiple related tags");
        Console.WriteLine("  ----------------------------");
        Console.WriteLine("    [InvalidatesCache(\"orders\", \"customers\", \"inventory\")]");
        Console.WriteLine("    public partial class OrderPlaced { ... }");
        Console.WriteLine();
        Console.WriteLine("    One event invalidates 3 tag groups.");
        Console.WriteLine("    Useful for cross-concern cache clearing.");
        Console.WriteLine();

        Console.WriteLine("  Usage in pipeline:");
        Console.WriteLine("  ─────────────────────");
        Console.WriteLine("    // In MutationInvoker/EventHandler pipeline:");
        Console.WriteLine("    // 1. Execute mutation → SaveChanges");
        Console.WriteLine("    // 2. Dispatch events");
        Console.WriteLine("    // 3. Event handler calls invalidator.InvalidateAsync(cache)");
        Console.WriteLine("    // 4. Next query hits DB (cache miss) and recaches");
        Console.WriteLine();
    }
}
