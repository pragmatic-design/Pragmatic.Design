namespace Pragmatic.Caching.Samples.Samples;

/// <summary>
///     Cache duration formats and options comparison: absolute vs sliding,
///     short-lived vs long-lived entries.
/// </summary>
public static class DurationComparisonSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. Duration Formats & Options Comparison");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  Duration format: \"5m\", \"1h\", \"1d\" (parsed at compile-time)");
        Console.WriteLine();

        ShowQueryComparison();
        ShowDiSetupPattern();

        Console.WriteLine();
    }

    private static void ShowQueryComparison()
    {
        Console.WriteLine("  5.1 Side-by-side query options");
        Console.WriteLine("  ----------------------------------");

        var queries = new (string Name, ICacheable Query)[]
        {
            ("GetUser (5m, absolute)", new GetUser { UserId = 1 }),
            ("GetDashboardStats (30m, sliding)", new GetDashboardStats { UserId = 1 }),
            ("GetProductDetails (1h, absolute)", new GetProductDetails { StoreId = 1, ProductId = 1 }),
            ("GetActiveUsers (5m, tagged)", new GetActiveUsers { TenantId = 42 })
        };

        foreach (var (name, query) in queries)
        {
            var opts = query.GetCacheOptions();
            var durStr = opts.SlidingDuration.HasValue
                ? $"Sliding={opts.SlidingDuration}"
                : $"Absolute={opts.Duration}";
            var tags = string.Join(", ", opts.Tags);
            var tagsStr = !string.IsNullOrEmpty(tags) ? $", Tags=[{tags}]" : "";
            Console.WriteLine($"    {name,-42} {durStr}{tagsStr}");
        }

        Console.WriteLine();
    }

    private static void ShowDiSetupPattern()
    {
        Console.WriteLine("  5.2 DI setup pattern");
        Console.WriteLine("  -----------------------");
        Console.WriteLine("""
            services.AddHybridCache();                // Microsoft L1+L2 cache
            services.AddPragmaticCaching(options =>
            {
                options.DefaultDuration = TimeSpan.FromMinutes(10);
                options.EnableQueryCaching = true;
                options.EnableEventInvalidation = true;
                options.KeyPrefix = "myapp:";         // Multi-app isolation
            });

            // Category routing (optional):
            services.AddPragmaticCaching(cache =>
            {
                cache.ForCategory<Permissions>(o => o.DefaultDuration = TimeSpan.FromMinutes(5));
                cache.ForCategory<OutputCache>(o => o.KeyPrefix = "oc:");
            });
        """);
        Console.WriteLine();
    }
}
