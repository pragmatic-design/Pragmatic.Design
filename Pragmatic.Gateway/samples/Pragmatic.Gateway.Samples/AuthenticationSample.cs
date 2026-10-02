namespace Pragmatic.Gateway.Samples;

/// <summary>
///     Configures the two authentication strategies the Gateway supports: JWT bearer
///     (<see cref="JwtOptions" />) and API key (<see cref="ApiKeyOptions" />).
///     Both are null-by-default — assigning them is what turns the strategy on.
/// </summary>
internal static class AuthenticationSample
{
    public static void Run()
    {
        SampleConsole.Header("JwtOptions / ApiKeyOptions — authentication");

        // ── JWT bearer, symmetric (HMAC) signing key ──────────────────────────────────────────
        // Mutually exclusive with JwksUrl. Setting Issuer/Audience auto-enables their validation.
        var hmacJwt = new JwtOptions
        {
            Issuer = "https://auth.example.com",
            Audience = "pragmatic-gateway",
            // SECURITY: never hard-code in source / appsettings.json. Load from a secrets manager.
            // Shown here only to make the sample self-contained.
            SigningKey = "this-is-a-demo-only-symmetric-signing-key-change-me",
            ForwardClaims = ["sub", "role", "tenant_id"]
        };

        SampleConsole.Section("JWT — HMAC symmetric key");
        SampleConsole.Item("Issuer (ValidateIssuer)", $"{hmacJwt.Issuer}  -> {hmacJwt.Issuer is not null}");
        SampleConsole.Item("Audience (ValidateAud)", $"{hmacJwt.Audience}  -> {hmacJwt.Audience is not null}");
        SampleConsole.Item("SigningKey", Redact(hmacJwt.SigningKey));
        SampleConsole.Item("JwksUrl", hmacJwt.JwksUrl ?? "(not used with symmetric key)");
        SampleConsole.Item("ForwardClaims", string.Join(", ", hmacJwt.ForwardClaims));

        // ── JWT bearer, asymmetric via OIDC/JWKS metadata ────────────────────────────────────
        var jwksJwt = new JwtOptions
        {
            Issuer = "https://login.microsoftonline.com/contoso/v2.0",
            Audience = "api://pragmatic-gateway",
            JwksUrl = "https://login.microsoftonline.com/contoso/v2.0/.well-known/openid-configuration"
        };

        SampleConsole.Section("JWT — asymmetric via JWKS/OIDC");
        SampleConsole.Item("JwksUrl (MetadataAddress)", jwksJwt.JwksUrl);
        SampleConsole.Item("SigningKey", jwksJwt.SigningKey ?? "(none — keys fetched from JWKS)");
        SampleConsole.Note("Gateway sets ValidateIssuerSigningKey=true even in JWKS mode, so alg=none / unsigned tokens are rejected.");

        // ── API key authentication ───────────────────────────────────────────────────────────
        var apiKey = new ApiKeyOptions
        {
            HeaderName = "X-Api-Key",
            // SECURITY: raw keys belong in a secrets manager, NOT appsettings.json.
            ValidKeys = ["demo-key-alpha", "demo-key-bravo"]
        };

        SampleConsole.Section("API key");
        SampleConsole.Item("HeaderName", apiKey.HeaderName);
        SampleConsole.Item("ValidKeys count", apiKey.ValidKeys.Count);
        SampleConsole.Item("ValidKeys", string.Join(", ", apiKey.ValidKeys.Select(Redact)));

        // Demonstrate the lookup the gateway performs for an inbound request header value.
        SampleConsole.Section("API key validation (illustrative lookup)");
        foreach (var candidate in new[] { "demo-key-alpha", "forged-key" })
        {
            var accepted = apiKey.ValidKeys.Contains(candidate);
            SampleConsole.Item($"presented '{Redact(candidate)}'", accepted ? "ACCEPTED" : "REJECTED");
        }
    }

    private static string Redact(string? secret)
        => string.IsNullOrEmpty(secret)
            ? "(empty)"
            : secret.Length <= 4 ? "****" : $"{secret[..2]}***{secret[^2..]}";
}
