namespace Pragmatic.Gateway.Samples;

/// <summary>
///     Configures the cross-cutting edge policies: rate limiting (<see cref="RateLimitOptions" />),
///     CORS (<see cref="CorsOptions" />) and TLS (<see cref="TlsOptions" />). Also reproduces the
///     startup guard the Gateway applies for the invalid "wildcard origin + credentials" CORS combo.
/// </summary>
internal static class RateLimitCorsTlsSample
{
    public static void Run()
    {
        SampleConsole.Header("RateLimitOptions / CorsOptions / TlsOptions");

        // ── Rate limiting (global fixed window) ──────────────────────────────────────────────
        var rateLimit = new RateLimitOptions
        {
            PermitLimit = 1_000,
            Window = TimeSpan.FromMinutes(1),
            KeyStrategy = "ip"
        };

        SampleConsole.Section("Rate limiting (fixed window)");
        SampleConsole.Item("PermitLimit", rateLimit.PermitLimit);
        SampleConsole.Item("Window", rateLimit.Window);
        SampleConsole.Item("KeyStrategy", rateLimit.KeyStrategy);
        SampleConsole.Item("Effective rate", $"{rateLimit.PermitLimit / rateLimit.Window.TotalSeconds:0.#} req/s per key");

        // ── CORS — safe wildcard (no credentials) ────────────────────────────────────────────
        var openCors = new CorsOptions { Origins = ["*"], Headers = ["*"], AllowCredentials = false };
        SampleConsole.Section("CORS — wildcard, no credentials (valid)");
        DescribeCors(openCors);
        SampleConsole.Item("Startup guard", ValidateCors(openCors));

        // ── CORS — explicit origins with credentials ─────────────────────────────────────────
        var credCors = new CorsOptions
        {
            Origins = ["https://app.example.com", "https://admin.example.com"],
            Headers = ["Authorization", "Content-Type"],
            Methods = ["GET", "POST"],
            AllowCredentials = true
        };
        SampleConsole.Section("CORS — explicit origins + credentials (valid)");
        DescribeCors(credCors);
        SampleConsole.Item("Startup guard", ValidateCors(credCors));

        // ── CORS — invalid combo the Gateway rejects at startup ───────────────────────────────
        var brokenCors = new CorsOptions { Origins = ["*"], AllowCredentials = true };
        SampleConsole.Section("CORS — wildcard + credentials (INVALID)");
        DescribeCors(brokenCors);
        SampleConsole.Item("Startup guard", ValidateCors(brokenCors));

        // ── TLS for the HTTPS listener ───────────────────────────────────────────────────────
        var pfx = new TlsOptions
        {
            CertificatePath = "/etc/pragmatic/certs/gateway.pfx",
            CertificatePassword = "demo-pfx-password" // SECURITY: load from secrets manager / env var.
        };
        var pem = new TlsOptions
        {
            CertificatePath = "/etc/pragmatic/certs/gateway.crt",
            KeyPath = "/etc/pragmatic/certs/gateway.key"
        };

        SampleConsole.Section("TLS — PKCS#12 (.pfx)");
        SampleConsole.Item("CertificatePath", pfx.CertificatePath);
        SampleConsole.Item("CertificatePassword", pfx.CertificatePassword is null ? "(none)" : "**** (redacted)");
        SampleConsole.Item("Load strategy", "X509CertificateLoader.LoadPkcs12FromFile");

        SampleConsole.Section("TLS — PEM pair (cert + key)");
        SampleConsole.Item("CertificatePath", pem.CertificatePath);
        SampleConsole.Item("KeyPath", pem.KeyPath);
        SampleConsole.Item("Load strategy", "X509Certificate2.CreateFromPemFile");
    }

    private static void DescribeCors(CorsOptions cors)
    {
        SampleConsole.Item("Origins", string.Join(", ", cors.Origins));
        SampleConsole.Item("Methods", string.Join(", ", cors.Methods));
        SampleConsole.Item("Headers", string.Join(", ", cors.Headers));
        SampleConsole.Item("AllowCredentials", cors.AllowCredentials);
    }

    // Mirrors the fail-fast check in Program.cs: wildcard origin + credentials is rejected.
    private static string ValidateCors(CorsOptions cors)
        => cors.Origins.Contains("*") && cors.AllowCredentials
            ? "REJECTED — '*' origin cannot be combined with AllowCredentials"
            : "OK";
}
