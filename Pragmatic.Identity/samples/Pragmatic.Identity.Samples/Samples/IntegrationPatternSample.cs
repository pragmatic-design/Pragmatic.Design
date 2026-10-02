namespace Pragmatic.Identity.Samples.Samples;

/// <summary>
///     Integration patterns: DI injection, claim mapping, ASP.NET Core bridge.
/// </summary>
public static class IntegrationPatternSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("2. Integration — DI, Claims, ASP.NET Core Bridge");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowDiInjection();
        ShowClaimMapping();
        ShowAuthSetup();

        Console.WriteLine();
    }

    private static void ShowDiInjection()
    {
        Console.WriteLine("  2.1 DI injection — ICurrentUser is scoped per request");
        Console.WriteLine("  --------------------------------------------------------");

        Console.WriteLine("""
            // Inject anywhere (services, mutations, handlers, endpoints):
            public class InvoiceService(ICurrentUser currentUser)
            {
                public async Task<Invoice> CreateAsync(CreateInvoiceDto dto)
                {
                    var invoice = new Invoice
                    {
                        CreatedBy = currentUser.Id,
                        TenantId = currentUser.TenantId
                    };

                    // Check permissions before sensitive operations
                    if (!currentUser.Authorization.HasPermission("billing.invoice.create"))
                        throw new ForbiddenException();

                    return invoice;
                }
            }
        """);
        Console.WriteLine();
    }

    private static void ShowClaimMapping()
    {
        Console.WriteLine("  2.2 Claim type mapping — IdentityOptions configuration");
        Console.WriteLine("  ---------------------------------------------------------");

        Console.WriteLine("""
            // Default claim types (customizable via IdentityOptions):
            services.AddPragmaticIdentity(options =>
            {
                options.UserIdClaimType = "sub";           // JWT "sub" claim
                options.DisplayNameClaimType = "name";     // Display name
                options.RoleClaimType = "role";             // Role assignment
                options.PermissionClaimType = "permission"; // Direct permissions
                options.TenantClaimType = "tenant_id";     // Multi-tenancy
            });

            // Multi-valued claims are supported:
            // JWT: { "role": ["admin", "editor"] }
            // → ICurrentUser.Claims["role"] = ["admin", "editor"]
            // → ICurrentUser.Authorization.Roles = ["admin", "editor"]
        """);
        Console.WriteLine();
    }

    private static void ShowAuthSetup()
    {
        Console.WriteLine("  2.3 Authentication setup in Program.cs");
        Console.WriteLine("  ------------------------------------------");

        Console.WriteLine("""
            // Development: NoOp handler (header-based identity)
            app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");

            // Production: JWT Bearer
            app.UseJwtAuthentication(jwt =>
            {
                jwt.SigningKey = "your-secret-key";
                jwt.Issuer = "https://auth.example.com";
                jwt.Audience = "my-api";
            });

            // The bridge: ClaimsPrincipalUserAccessor maps
            // HttpContext.User (ClaimsPrincipal) → ICurrentUser
            // This happens automatically per request — no manual wiring.
        """);
        Console.WriteLine();
    }
}
