namespace Pragmatic.Authorization.Samples.Samples;

/// <summary>
///     Role and permission patterns: IRole, IRoleDefinition, AuthorizationBuilder wiring,
///     permission provider chain, and IResourceAuthorizer.
/// </summary>
public static class RolePermissionSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("3. Roles & Permissions — Provider Chain Architecture");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowRoleDefinition();
        ShowProviderChain();
        ShowAuthorizationBuilder();
        ShowResourceAuthorizer();

        Console.WriteLine();
    }

    private static void ShowRoleDefinition()
    {
        Console.WriteLine("  3.1 Strongly-typed roles and role definitions");
        Console.WriteLine("  -------------------------------------------------");

        Console.WriteLine("""
            // IRole — application role with default permissions
            public sealed class BookingManager : IRole
            {
                public static string Name => "booking-manager";
                public static string? Description => "Full booking operations";
                public static IReadOnlyList<string> DefaultPermissions =>
                    ["booking.*", "catalog.*.read"];
            }

            // IRoleDefinition — reusable permission template
            public sealed class CatalogReader : IRoleDefinition
            {
                public static string Name => "catalog-reader";
                public static IReadOnlyList<string> Permissions =>
                    ["catalog.amenity.read", "catalog.property.read", "catalog.room-type.read"];
            }

            // Roles can include definitions:
            authz.MapRole<BookingManager>(r => r
                .IncludeDefinition<CatalogReader>()   // Adds catalog read perms
                .WithoutPermissions("booking.*.delete")); // Remove specific perms
        """);
        Console.WriteLine();
    }

    private static void ShowProviderChain()
    {
        Console.WriteLine("  3.2 Permission provider chain (3-layer resolution)");
        Console.WriteLine("  ----------------------------------------------------");

        Console.WriteLine("    Layer 1: ClaimsPermissionProvider (Order=0)");
        Console.WriteLine("      → Direct \"permission\" claims from JWT/header");
        Console.WriteLine();
        Console.WriteLine("    Layer 2: RoleExpansionProvider (Order=100)");
        Console.WriteLine("      → Claims \"role\" → IRolePermissionStore → permissions");
        Console.WriteLine("      → WildcardMatcher expands \"booking.*\" at check time");
        Console.WriteLine();
        Console.WriteLine("    Layer 3: GroupExpansionProvider (Order=200)");
        Console.WriteLine("      → Claims \"group\" → IGroupRoleStore → roles → permissions");
        Console.WriteLine();
        Console.WriteLine("    All results merged (union) → cached per request");
        Console.WriteLine("    Optional cross-request HybridCache for expensive expansions");
        Console.WriteLine();
    }

    private static void ShowAuthorizationBuilder()
    {
        Console.WriteLine("  3.3 AuthorizationBuilder — fluent DI configuration");
        Console.WriteLine("  -----------------------------------------------------");

        Console.WriteLine("""
            app.UseAuthorization(authz =>
            {
                // Endpoints are authenticated by default via
                // PragmaticEndpointsOptions.RequireAuthorizationByDefault.

                // Strongly-typed role mapping
                authz.MapRole<ShowcaseAdmin>();
                authz.MapRole<BookingManager>(r => r
                    .IncludeDefinition<CatalogReader>());

                // String-based role (runtime-defined)
                authz.MapRole("auditor", r => r
                    .WithOperation<BookingBoundary>(CrudOperation.Read));

                // Groups → Roles expansion
                authz.MapGroup<CustomerCareGroup>();

                // Resource-level authorization (ABAC)
                authz.AddResourceAuthorizer<InvoiceAuthorizer, Invoice>();

                // Cross-request permission caching
                authz.UsePermissionCache(TimeSpan.FromMinutes(5));
            });
        """);
        Console.WriteLine();
    }

    private static void ShowResourceAuthorizer()
    {
        Console.WriteLine("  3.4 IResourceAuthorizer<T> — instance-level authorization (ABAC)");
        Console.WriteLine("  -------------------------------------------------------------------");

        Console.WriteLine("""
            // Per-entity authorization (e.g., can this user access THIS invoice?)
            public class InvoiceAuthorizer : IResourceAuthorizer<Invoice>
            {
                public ValueTask<bool> CanAccessAsync(
                    ICurrentUser user, Invoice invoice, string action, CancellationToken ct)
                {
                    // Owner always has access
                    if (invoice.OwnerId == user.Id) return ValueTask.FromResult(true);

                    // Admin permission grants full access
                    if (user.Authorization.HasPermission("billing.invoice.admin"))
                        return ValueTask.FromResult(true);

                    // Read-only for users with scoped access
                    if (action == "read" && invoice.AccessScopes.Any(s =>
                        user.Authorization.HasPermission($"scope:{s}")))
                        return ValueTask.FromResult(true);

                    return ValueTask.FromResult(false);
                }
            }

            // Registered via: authz.AddResourceAuthorizer<InvoiceAuthorizer, Invoice>();
            // Applied automatically by ResourceAuthorizationFilter (Order 250)
        """);
        Console.WriteLine();
    }
}
