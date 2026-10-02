using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Pragmatic.Identity.Local.Jwt;

namespace Pragmatic.Identity.Samples.Samples;

/// <summary>
///     Identity.Local.Jwt — runnable JWT generation, claim inspection, and the
///     startup safety guards that protect signing keys and the login endpoint.
///
///     <see cref="JwtTokenGenerator"/> is the real production type and runs here
///     without a host: it signs an HMAC-SHA256 token from a subject + claims.
///     <see cref="JwtOptionsValidator"/> is the real <c>IValidateOptions</c> that the
///     <c>UseJwtAuthentication()</c> builder wires with <c>ValidateOnStart()</c>; we
///     invoke it directly to show the fail-fast behaviour without standing up Kestrel.
/// </summary>
public static class JwtTokenSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. Identity.Local.Jwt — Token generation, claims, startup guards");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        GenerateAndInspectToken();
        ShowSecurityStampClaim();
        ShowOptionsValidation();
        ShowRateLimitDefaults();
        ShowBuilderWiring();

        Console.WriteLine();
    }

    // ===== 6.1 Generate a token and read its claims =========================
    private static void GenerateAndInspectToken()
    {
        Console.WriteLine("  6.1 JwtTokenGenerator.Generate");
        Console.WriteLine("  ---------------------------------");

        // A >= 32-byte key is mandatory for HMAC-SHA256; the generator rejects shorter keys.
        var options = Options.Create(new JwtOptions
        {
            SigningKey = "this-is-a-demo-signing-key-32-bytes!!",
            Issuer = "https://auth.sample.local",
            Audience = "sample-api",
            TokenExpiration = TimeSpan.FromHours(2)
        });
        var generator = new JwtTokenGenerator(options);

        var result = generator.Generate(
            subject: "local|alice@example.com",
            displayName: "Alice Example",
            tenantId: "tenant-42",
            roles: ["docs-editor", "board-member"],
            permissions: ["docs.read", "docs.write"]);

        Console.WriteLine($"     token issued, expires at : {result.ExpiresAt:u}");
        Console.WriteLine($"     token length             : {result.Token.Length} chars");

        // Read the claims back from the signed token (handler reused statically inside the generator).
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Console.WriteLine($"     iss                      : {jwt.Issuer}");
        Console.WriteLine($"     aud                      : {string.Join(", ", jwt.Audiences)}");
        Console.WriteLine($"     sub                      : {ClaimValue(jwt, "sub")}");
        Console.WriteLine($"     name                     : {ClaimValue(jwt, "name")}");
        Console.WriteLine($"     tenant_id                : {ClaimValue(jwt, "tenant_id")}");
        Console.WriteLine($"     role (multi)             : {string.Join(", ", ClaimValues(jwt, "role"))}");
        Console.WriteLine($"     permission (multi)       : {string.Join(", ", ClaimValues(jwt, "permission"))}");
        Console.WriteLine();
    }

    // ===== 6.2 Security-stamp claim (token revocation) =====================
    private static void ShowSecurityStampClaim()
    {
        Console.WriteLine("  6.2 Security stamp claim (sstamp) — token revocation");
        Console.WriteLine("  ------------------------------------------------------");

        var generator = new JwtTokenGenerator(Options.Create(new JwtOptions
        {
            SigningKey = "this-is-a-demo-signing-key-32-bytes!!"
        }));

        var token = generator.Generate("local|alice@example.com", securityStamp: "stamp-v1");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);

        Console.WriteLine($"     sstamp embedded          : {ClaimValue(jwt, "sstamp")}");
        Console.WriteLine("     On each request the JwtBearer handler re-loads the identity and compares");
        Console.WriteLine("     its current SecurityStamp to this claim. A password change/reset rotates");
        Console.WriteLine("     the stamp → previously-issued tokens fail validation (fail-closed).");
        Console.WriteLine();
    }

    // ===== 6.3 Signing-key guard ============================================
    private static void ShowOptionsValidation()
    {
        Console.WriteLine("  6.3 Signing-key guard — weak keys rejected");
        Console.WriteLine("  --------------------------------------------");

        // The generator enforces the >= 32-byte (256-bit) HMAC-SHA256 minimum at runtime.
        // The same rule is enforced at startup by the internal JwtOptionsValidator that
        // UseJwtAuthentication() registers with ValidateOnStart(), plus the builder-time guard.
        var weak = new JwtTokenGenerator(Options.Create(new JwtOptions { SigningKey = "too-short" }));
        try
        {
            weak.Generate("local|alice@example.com");
            Console.WriteLine("     short signing key        : UNEXPECTED — no exception");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"     short signing key        : REJECTED ({ex.Message.Split('.')[0]})");
        }

        var strong = new JwtTokenGenerator(Options.Create(new JwtOptions
        {
            SigningKey = "this-is-a-demo-signing-key-32-bytes!!"
        }));
        var ok = strong.Generate("local|alice@example.com");
        Console.WriteLine($"     32-byte signing key      : ACCEPTED (token len {ok.Token.Length})");
        Console.WriteLine();
    }

    // ===== 6.4 Login rate-limit defaults ====================================
    private static void ShowRateLimitDefaults()
    {
        Console.WriteLine("  6.4 LoginRateLimitOptions — IP rate limiting (defense in depth)");
        Console.WriteLine("  -----------------------------------------------------------------");

        var rl = new LoginRateLimitOptions();
        Console.WriteLine($"     Enabled                  : {rl.Enabled}");
        Console.WriteLine($"     PermitLimit / Window     : {rl.PermitLimit} requests / {rl.Window}");
        Console.WriteLine($"     RejectionStatusCode      : {rl.RejectionStatusCode}");
        Console.WriteLine("     Read from config 'Identity:Local:RateLimit' by UseJwtAuthentication(); applied to the");
        Console.WriteLine("     exposed LoginUser / SignInUser endpoints, whatever route they were given.");
        Console.WriteLine();
    }

    // ===== 6.5 Host wiring (setup-only) =====================================
    private static void ShowBuilderWiring()
    {
        Console.WriteLine("  6.5 Host wiring — UseJwtAuthentication (in Program.cs of a host)");
        Console.WriteLine("  -----------------------------------------------------------------");

        Console.WriteLine("""
            // builder is IPragmaticBuilder (Pragmatic.Composition)
            builder.UseJwtAuthentication(jwt =>
            {
                jwt.SigningKey = builder.Configuration["Jwt:Key"]!;   // >= 32 bytes, enforced at startup
                jwt.Issuer     = "https://auth.example.com";          // required in Production
                jwt.Audience   = "my-api";                            // required in Production
                jwt.TokenExpiration = TimeSpan.FromHours(1);
            });

            // Wires: JwtTokenGenerator (singleton), JwtBearer validation, IValidateOptions<JwtOptions>
            // (ValidateOnStart), the sstamp revocation event, and AddPragmaticAuthorization().
            // RequireHttpsMetadata is forced on in Production.
        """);
    }

    // ===== Helpers ==========================================================

    private static string ClaimValue(JwtSecurityToken jwt, string type) =>
        jwt.Claims.FirstOrDefault(c => c.Type == type)?.Value ?? "(none)";

    private static IEnumerable<string> ClaimValues(JwtSecurityToken jwt, string type) =>
        jwt.Claims.Where(c => c.Type == type).Select(c => c.Value);
}
