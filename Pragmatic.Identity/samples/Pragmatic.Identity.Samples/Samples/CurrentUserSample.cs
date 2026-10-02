using Pragmatic.Identity;

namespace Pragmatic.Identity.Samples.Samples;

/// <summary>
///     ICurrentUser: property composition, PrincipalKind, built-in singletons.
/// </summary>
public static class CurrentUserSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("1. ICurrentUser — Identity Context & Built-in Singletons");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowAnonymousUser();
        ShowSystemUser();
        ShowPrincipalKinds();
        ShowPropertyComposition();

        Console.WriteLine();
    }

    private static void ShowAnonymousUser()
    {
        Console.WriteLine("  1.1 AnonymousUser — fallback for unauthenticated requests");
        Console.WriteLine("  ------------------------------------------------------------");

        var anon = AnonymousUser.Instance;
        Console.WriteLine($"    Id:              \"{anon.Id}\" (empty)");
        Console.WriteLine($"    DisplayName:     {(anon.DisplayName is null ? "(null)" : anon.DisplayName)}");
        Console.WriteLine($"    IsAuthenticated: {anon.IsAuthenticated}");
        Console.WriteLine($"    Kind:            {anon.Kind}");
        Console.WriteLine($"    TenantId:        {(anon.TenantId is null ? "(null)" : anon.TenantId)}");
        Console.WriteLine();
    }

    private static void ShowSystemUser()
    {
        Console.WriteLine("  1.2 SystemUser — background jobs, seed, migrations");
        Console.WriteLine("  -----------------------------------------------------");

        var system = SystemUser.Instance;
        Console.WriteLine($"    Id:              \"{system.Id}\"");
        Console.WriteLine($"    DisplayName:     \"{system.DisplayName}\"");
        Console.WriteLine($"    IsAuthenticated: {system.IsAuthenticated}");
        Console.WriteLine($"    Kind:            {system.Kind}");
        Console.WriteLine();
        Console.WriteLine("    SystemUser has full access — all permission checks return true.");
        Console.WriteLine("    Use for: background jobs, data seed, migration scripts.");
        Console.WriteLine();
    }

    private static void ShowPrincipalKinds()
    {
        Console.WriteLine("  1.3 PrincipalKind — 4 types of identity");
        Console.WriteLine("  -------------------------------------------");

        Console.WriteLine($"    {PrincipalKind.Anonymous,-12} — No authenticated identity");
        Console.WriteLine($"    {PrincipalKind.User,-12} — Human via identity provider (JWT, cookie)");
        Console.WriteLine($"    {PrincipalKind.Service,-12} — Service-to-service (API key, client credentials)");
        Console.WriteLine($"    {PrincipalKind.System,-12} — System context (background jobs, migrations)");
        Console.WriteLine();
    }

    private static void ShowPropertyComposition()
    {
        Console.WriteLine("  1.4 Property composition — separated concerns");
        Console.WriteLine("  ---------------------------------------------------");

        Console.WriteLine("""
            ICurrentUser
            ├── .Id, .DisplayName, .IsAuthenticated, .Kind, .TenantId
            ├── .Claims — multi-valued claim dictionary
            ├── .Delegation     — who is acting on this user's behalf (null when nobody is)
            │
            ├── .Authorization (IUserAuthorization)
            │   ├── .Roles, .Permissions, .Groups, .Scopes
            │   ├── .HasPermission("booking.reservation.create")
            │   ├── .HasAnyPermission(["perm.a", "perm.b"])
            │   ├── .HasAllPermissions(["perm.a", "perm.b"])
            │   ├── .IsInRole("admin")
            │   └── .IsInGroup("customer-care")
            │
            └── .Authentication (IAuthenticationContext)
                ├── .Scheme — "Bearer", "Cookie", etc.
                ├── .Protocol — "oidc", "saml2", "apikey"
                ├── .Issuer — IdP URL
                ├── .IsMfaAuthenticated
                ├── .AuthenticatedAt, .ExpiresAt
                └── .ExternalIdentityKey — "{issuer}|{subject}"
        """);
        Console.WriteLine();
    }
}
