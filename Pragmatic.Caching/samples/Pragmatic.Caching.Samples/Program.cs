using Pragmatic.Caching.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Caching Samples                        ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ── SG-Generated API ─────────────────────────────────────────────────────────

// 1. Cache key generation: basic, multi-property, custom names, static helpers
CacheKeySample.Run();

// 2. Cache options: duration, sliding expiration, tags with placeholders
CacheOptionsSample.Run();

// 3. Cache invalidation: event-driven, convention-based, explicit tags/keys
InvalidationSample.Run();

// ── Real Cache Operations ────────────────────────────────────────────────────

// 4. ICacheStack: GetOrSetAsync (hit/miss), tag invalidation, key removal
await CacheHitMissSample.RunAsync();

// 5. Duration formats and DI setup patterns
DurationComparisonSample.Run();

// 6. Category routing: ForCategory<T>() registration + CacheStackProvider resolution
await CategoryRoutingSample.RunAsync();

// 7. Conditional caching: CacheFactoryResult<T> Cache vs DoNotCache
await ConditionalCachingSample.RunAsync();

// 8. Observability: ActivitySource + Meter listeners over real cache operations
await ObservabilitySample.RunAsync();

// 9. Live invalidation via the SG-generated ICacheInvalidator.InvalidateAsync()
await LiveInvalidationSample.RunAsync();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
