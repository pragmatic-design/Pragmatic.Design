namespace Pragmatic.Authorization.Samples.Samples;

/// <summary>
///     WildcardMatcher: permission pattern matching for hierarchical permission systems.
///     Supports wildcard segments at any level.
/// </summary>
public static class WildcardMatcherSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("2. WildcardMatcher — Hierarchical Permission Patterns");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowBasicMatching();
        ShowHierarchicalPatterns();

        Console.WriteLine();
    }

    private static void ShowBasicMatching()
    {
        Console.WriteLine("  2.1 Basic wildcard matching");
        Console.WriteLine("  ------------------------------");

        // WildcardMatcher is internal — used by CachedPermissionResolver
        // These are the matching rules it follows:
        Console.WriteLine("    Pattern                          Permission                          Match");
        Console.WriteLine("    ───────────────────────────────  ──────────────────────────────────  ─────");
        Console.WriteLine("    \"booking.*\"                      \"booking.reservation.create\"        true");
        Console.WriteLine("    \"booking.*\"                      \"billing.invoice.read\"              false");
        Console.WriteLine("    \"*.read\"                         \"booking.reservation.read\"          true");
        Console.WriteLine("    \"*.read\"                         \"booking.reservation.delete\"        false");
        Console.WriteLine("    \"*\"                              \"anything.at.all\"                   true");
        Console.WriteLine("    \"booking.reservation.create\"     \"booking.reservation.create\"        true");
        Console.WriteLine("    \"booking.reservation.create\"     \"booking.reservation.delete\"        false");

        Console.WriteLine();
    }

    private static void ShowHierarchicalPatterns()
    {
        Console.WriteLine("  2.2 Hierarchical permission patterns");
        Console.WriteLine("  ----------------------------------------");

        Console.WriteLine("""
            Permission naming convention: {boundary}.{entity}.{operation}

            Examples:
              booking.reservation.create    — Create reservations
              booking.reservation.*         — All reservation operations
              booking.*                     — All booking operations
              booking.*.read                — Read any booking entity
              *                             — Full system access (super admin)

            Use in role definitions:
              IRole.DefaultPermissions = ["booking.*"]        — full boundary access
              IRoleDefinition.Permissions = ["booking.*.read"] — read-only across boundary
        """);
        Console.WriteLine();
    }
}
