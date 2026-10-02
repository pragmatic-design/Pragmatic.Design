using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Identity;

namespace Pragmatic.Identity.Samples.Samples;

/// <summary>
///     Identity.AspNetCore — runnable demonstration of the ClaimsPrincipal → ICurrentUser
///     bridge and the configurable claim mapping, plus the dev-only header/no-op auth pieces.
///
///     <see cref="ClaimsPrincipalUserAccessor"/> is the real production type that maps
///     <c>HttpContext.User</c> to <see cref="ICurrentUser"/>. We drive it with a real
///     <see cref="HttpContextAccessor"/> carrying a hand-built <see cref="ClaimsPrincipal"/>,
///     so no Kestrel/host is needed. <see cref="IdentityOptions"/> claim-type mapping is
///     exercised against actual claims (not shown as comments).
/// </summary>
public static class AspNetCoreClaimsSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. Identity.AspNetCore — ClaimsPrincipal → ICurrentUser bridge");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowAccessorWithDefaultClaims();
        ShowCustomClaimMapping();
        ShowDevOnlyComponents();

        Console.WriteLine();
    }

    // ===== 7.1 Default claim mapping ========================================
    private static void ShowAccessorWithDefaultClaims()
    {
        Console.WriteLine("  7.1 ClaimsPrincipalUserAccessor — default claim types");
        Console.WriteLine("  -------------------------------------------------------");

        var options = new IdentityOptions(); // default XML-schema claim URIs
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(options.UserIdClaimType, "user-123"),
                new Claim(options.DisplayNameClaimType, "Alice Example"),
                new Claim(options.TenantClaimType, "tenant-42"),
                new Claim(options.RoleClaimType, "docs-editor"),
                new Claim(options.PermissionClaimType, "docs.read"),
                new Claim("iss", "https://auth.sample.local"),
                new Claim("amr", "mfa")
            ],
            authenticationType: "Bearer"));

        ICurrentUser user = BuildAccessor(principal, options);

        Console.WriteLine($"     Id                       : {user.Id}");
        Console.WriteLine($"     DisplayName              : {user.DisplayName}");
        Console.WriteLine($"     IsAuthenticated / Kind   : {user.IsAuthenticated} / {user.Kind}");
        Console.WriteLine($"     TenantId                 : {user.TenantId}");
        Console.WriteLine($"     claims['role']           : [{string.Join(", ", user.Claims.GetValueOrDefault("role") ?? [])}]");
        Console.WriteLine($"     Authentication.Scheme    : {user.Authentication.Scheme}");
        Console.WriteLine($"     Authentication.Issuer    : {user.Authentication.Issuer}");
        Console.WriteLine($"     IsMfaAuthenticated       : {user.Authentication.IsMfaAuthenticated}");
        Console.WriteLine($"     ExternalIdentityKey      : {user.Authentication.ExternalIdentityKey}");
        Console.WriteLine();
    }

    // ===== 7.2 Custom claim mapping (e.g. JWT short names) ==================
    private static void ShowCustomClaimMapping()
    {
        Console.WriteLine("  7.2 IdentityOptions — custom claim-type mapping (JWT short names)");
        Console.WriteLine("  ------------------------------------------------------------------");

        // A JWT provider issues short claim names. Point IdentityOptions at them and the
        // accessor reads exactly the same way — no consumer code changes.
        var options = new IdentityOptions
        {
            UserIdClaimType = "sub",
            DisplayNameClaimType = "name",
            RoleClaimType = "role",
            PermissionClaimType = "permission",
            TenantClaimType = "tenant_id"
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "local|bob@example.com"),
                new Claim("name", "Bob"),
                new Claim("tenant_id", "tenant-7"),
                new Claim("role", "admin"),
                new Claim("role", "editor")  // multi-valued
            ],
            authenticationType: "Bearer"));

        ICurrentUser user = BuildAccessor(principal, options);

        Console.WriteLine($"     Id (from 'sub')          : {user.Id}");
        Console.WriteLine($"     DisplayName (from 'name'): {user.DisplayName}");
        Console.WriteLine($"     TenantId (from tenant_id): {user.TenantId}");
        Console.WriteLine($"     roles (normalized 'role'): [{string.Join(", ", user.Claims.GetValueOrDefault("role") ?? [])}]");
        Console.WriteLine();
    }

    // ===== 7.3 Dev-only components (setup-only) =============================
    private static void ShowDevOnlyComponents()
    {
        Console.WriteLine("  7.3 HeaderUserMiddleware + NoOpAuthenticationHandler (DEV ONLY)");
        Console.WriteLine("  -----------------------------------------------------------------");

        Console.WriteLine("""
            // Development pipeline — fakes an identity from request headers so you can call
            // secured endpoints without a real IdP. BOTH types THROW if used outside the
            // Development environment (IHostEnvironment.IsDevelopment() guard) — they cannot
            // be accidentally shipped to production.

            // 1. Register the no-op scheme:
            builder.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");

            // 2. Insert the header middleware BEFORE UseAuthentication():
            if (app.Environment.IsDevelopment())
                app.UseMiddleware<HeaderUserMiddleware>();

            // 3. Send identity via headers:
            //    X-User-Id: user-123
            //    X-User-Name: Alice
            //    X-User-Roles: docs-editor,board-member   (comma-separated → multiple claims)
            //    X-User-Permissions: docs.read,docs.write
            //    X-User-Tenant: tenant-42
            //    X-User-Groups: engineering
            //
            // HeaderUserMiddleware builds a ClaimsPrincipal from these; NoOpAuthenticationHandler
            // turns it into an authenticated ticket; ClaimsPrincipalUserAccessor then resolves it
            // to ICurrentUser — the same bridge demonstrated live in 7.1/7.2.
        """);
    }

    // ===== Helper: build the real accessor without a host ===================

    /// <summary>
    ///     Constructs the production <see cref="ClaimsPrincipalUserAccessor"/> with a real
    ///     <see cref="HttpContextAccessor"/>. The accessor resolves <c>IUserAuthorization</c>
    ///     lazily from the service provider; this sample never touches <c>.Authorization</c>, so
    ///     an empty provider suffices (see GroupsAndRolesSample for the full authorization graph).
    /// </summary>
    private static ClaimsPrincipalUserAccessor BuildAccessor(ClaimsPrincipal principal, IdentityOptions options)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        IServiceProvider emptyProvider = new ServiceCollection().BuildServiceProvider();
        return new ClaimsPrincipalUserAccessor(accessor, Options.Create(options), emptyProvider);
    }
}
