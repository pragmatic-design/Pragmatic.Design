namespace Pragmatic.Caching.Samples.Samples;

/// <summary>
///     Cache options: duration, sliding expiration, tags with placeholders.
/// </summary>
public static class CacheOptionsSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("2. Cache Options — Duration, Sliding, Tags");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  2.1 Absolute duration (default)");
        Console.WriteLine("  ----------------------------------");
        var getUser = new GetUser { UserId = 1 };
        var opts1 = getUser.GetCacheOptions();
        Console.WriteLine($"    [Cacheable(Duration = \"5m\")]");
        Console.WriteLine($"    Duration: {opts1.Duration}");
        Console.WriteLine($"    Entry expires 5 minutes after being cached, regardless of access.");
        Console.WriteLine();

        Console.WriteLine("  2.2 Sliding expiration");
        Console.WriteLine("  -------------------------");
        var dashboard = new GetDashboardStats { UserId = 1 };
        var opts2 = dashboard.GetCacheOptions();
        Console.WriteLine($"    [Cacheable(Duration = \"30m\", Sliding = true)]");
        Console.WriteLine($"    SlidingDuration: {opts2.SlidingDuration}");
        Console.WriteLine($"    Timer resets on each access — stays cached as long as it's used.");
        Console.WriteLine();

        Console.WriteLine("  2.3 Tags with placeholders — tenant isolation");
        Console.WriteLine("  -------------------------------------------------");
        var activeUsers = new GetActiveUsers { TenantId = 42 };
        var opts3 = activeUsers.GetCacheOptions();
        Console.WriteLine($"    [Cacheable(Tags = [\"users\", \"tenant:{{TenantId}}\"])]");
        Console.WriteLine($"    Tags: [{string.Join(", ", opts3.Tags)}]");
        Console.WriteLine($"    Placeholder {{TenantId}} expanded to 42 at construction time.");
        Console.WriteLine($"    InvalidateByTagAsync(\"tenant:42\") clears all this tenant's cache.");
        Console.WriteLine();
    }
}
