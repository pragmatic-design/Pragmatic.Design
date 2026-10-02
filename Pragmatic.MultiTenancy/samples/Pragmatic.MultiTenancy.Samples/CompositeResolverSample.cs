using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.MultiTenancy;

namespace Pragmatic.MultiTenancy.Samples;

/// <summary>
///     Demonstrates <see cref="CompositeTenantResolver"/>: tries an ordered set of resolvers and
///     returns the first non-null result, skipping resolvers that return null or throw.
/// </summary>
public static class CompositeResolverSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. CompositeTenantResolver — chain of responsibility (executed)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // 7.1 First resolver wins.
        Console.WriteLine("  7.1 First non-null wins");
        Console.WriteLine("  -----------------------");
        var firstWins = new CompositeTenantResolver(
            [Fixed("from-header"), Fixed("from-claim")],
            NullLogger<CompositeTenantResolver>.Instance);
        Console.WriteLine($"    [header=acme, claim=acme] → \"{await firstWins.ResolveAsync()}\"");
        Console.WriteLine();

        // 7.2 Falls through nulls to the next resolver.
        Console.WriteLine("  7.2 Null resolvers are skipped → fall through to next");
        Console.WriteLine("  -----------------------------------------------------");
        var fallThrough = new CompositeTenantResolver(
            [Fixed(null), Fixed(null), Fixed("from-subdomain")],
            NullLogger<CompositeTenantResolver>.Instance);
        Console.WriteLine($"    [null, null, subdomain]   → \"{await fallThrough.ResolveAsync()}\"");
        Console.WriteLine();

        // 7.3 A throwing resolver is logged and skipped — chain continues.
        Console.WriteLine("  7.3 A throwing resolver is caught & skipped → chain continues");
        Console.WriteLine("  ------------------------------------------------------------");
        var resilient = new CompositeTenantResolver(
            [Throwing(), Fixed("recovered")],
            NullLogger<CompositeTenantResolver>.Instance);
        Console.WriteLine($"    [throws, fixed]           → \"{await resilient.ResolveAsync()}\"");
        Console.WriteLine();

        // 7.4 Nobody resolves → null.
        Console.WriteLine("  7.4 No resolver succeeds → null");
        Console.WriteLine("  -------------------------------");
        var noneWin = new CompositeTenantResolver(
            [Fixed(null), Fixed(null)],
            NullLogger<CompositeTenantResolver>.Instance);
        var result = await noneWin.ResolveAsync();
        Console.WriteLine($"    [null, null]              → {(result is null ? "null" : $"\"{result}\"")}");
        Console.WriteLine();
    }

    private static ITenantResolver Fixed(string? value) => new FixedResolver(value);
    private static ITenantResolver Throwing() => new ThrowingResolver();

    private sealed class FixedResolver(string? value) : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(value);
    }

    private sealed class ThrowingResolver : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated resolver failure");
    }
}
