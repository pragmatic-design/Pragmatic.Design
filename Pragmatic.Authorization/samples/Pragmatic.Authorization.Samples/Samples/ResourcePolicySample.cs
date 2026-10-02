using Pragmatic.Authorization.Policy;

namespace Pragmatic.Authorization.Samples.Samples;

/// <summary>
///     ResourcePolicy composition: composable authorization rules with operators.
///     Same composition pattern as Specification but for authorization decisions.
/// </summary>
public static class ResourcePolicySample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("1. ResourcePolicy — Composable Authorization Rules");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowBasicPolicies();
        ShowComposition();
        ShowRealWorldPolicies();

        Console.WriteLine();
    }

    private static void ShowBasicPolicies()
    {
        Console.WriteLine("  1.1 Built-in policy factories");
        Console.WriteLine("  --------------------------------");

        Console.WriteLine($"    ResourcePolicy.Allow          — always grants access");
        Console.WriteLine($"    ResourcePolicy.Deny           — always denies access");
        Console.WriteLine($"    IsAuthenticated()             — requires authenticated user");
        Console.WriteLine($"    RequirePermission(\"p\")        — requires specific permission");
        Console.WriteLine($"    RequireAnyPermission(\"a\",\"b\") — requires at least one");
        Console.WriteLine($"    RequireAllPermissions(\"a\",\"b\")— requires all");
        Console.WriteLine($"    InRole(\"admin\")               — requires role membership");
        Console.WriteLine($"    InGroup(\"team\")               — requires group membership");
        Console.WriteLine($"    HasPrincipalKind(Service)     — requires specific principal type");
        Console.WriteLine();
    }

    private static void ShowComposition()
    {
        Console.WriteLine("  1.2 Composition with & | ! operators");
        Console.WriteLine("  ----------------------------------------");

        Console.WriteLine("""
            // AND: both must pass
            var adminOnlyWrite = RequirePermission("orders.write") & InRole("admin");

            // OR: at least one must pass
            var serviceOrAdmin = HasPrincipalKind(Service) | InRole("admin");

            // NOT: negate a policy
            var notGuest = !InRole("guest");

            // Complex: service principals OR (authenticated AND has permission)
            var readPolicy = HasPrincipalKind(Service)
                           | (IsAuthenticated() & RequirePermission("orders.read"));
        """);
        Console.WriteLine();
    }

    private static void ShowRealWorldPolicies()
    {
        Console.WriteLine("  1.3 Real-world policy patterns");
        Console.WriteLine("  ----------------------------------");

        Console.WriteLine("""
            // Reservation management: service accounts bypass, users need permission
            public static readonly ResourcePolicy ReservationManagement =
                ResourcePolicy.HasPrincipalKind(PrincipalKind.Service)
                | (ResourcePolicy.IsAuthenticated()
                   & ResourcePolicy.RequirePermission("booking.reservation.create"));

            // Read-only: any authenticated user
            public static readonly ResourcePolicy ReadOnly =
                ResourcePolicy.IsAuthenticated();

            // Admin-only: role-based
            public static readonly ResourcePolicy AdminOnly =
                ResourcePolicy.InRole("admin");

            // Multi-tenant isolation: must belong to tenant group
            public static readonly ResourcePolicy TenantIsolated =
                ResourcePolicy.IsAuthenticated()
                & ResourcePolicy.InGroup("tenant-operators");
        """);
        Console.WriteLine();
    }
}
