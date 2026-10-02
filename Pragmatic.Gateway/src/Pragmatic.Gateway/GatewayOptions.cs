namespace Pragmatic.Gateway;

/// <summary>
///     Complete configuration for the Pragmatic Gateway.
///     Loaded from pragmatic-gateway.yaml, appsettings.json, env vars, or CLI args.
/// </summary>
public sealed class GatewayOptions
{
    /// <summary>HTTP listen URL. Default: http://*:8080.</summary>
    public string HttpUrl { get; set; } = "http://*:8080";

    /// <summary>HTTPS listen URL. Null = no HTTPS. Example: https://*:8443.</summary>
    public string? HttpsUrl { get; set; }

    /// <summary>TLS certificate configuration.</summary>
    public TlsOptions Tls { get; set; } = new();

    /// <summary>Agent socket path.</summary>
    public string AgentSocketPath { get; set; } = OperatingSystem.IsWindows()
        ? "pragmatic-agent" : "/var/run/pragmatic/agent.sock";

    /// <summary>Agent instance name.</summary>
    public string? AgentInstance { get; set; }

    /// <summary>Static routes (fallback when Agent unavailable).</summary>
    public List<RouteEntry> Routes { get; set; } = [];

    /// <summary>JWT authentication. Null = disabled.</summary>
    public JwtOptions? Jwt { get; set; }

    /// <summary>
    ///     Trust the request Host header to derive the tenant from its subdomain
    ///     (<c>acme.myapp.com</c> → <c>acme</c>) when no signed tenant claim is present. Default
    ///     <c>false</c>: the Host header is client-controllable unless the deployment enforces host
    ///     filtering, so subdomain-based tenant resolution is opt-in. Enable it ONLY behind a proxy /
    ///     ASP.NET host filtering that guarantees the Host cannot be spoofed. When disabled, the tenant
    ///     is resolved exclusively from the signed JWT <c>tenant_id</c> claim (unspoofable).
    /// </summary>
    public bool TrustHostForTenant { get; set; }

    /// <summary>API key authentication. Null = disabled.</summary>
    public ApiKeyOptions? ApiKey { get; set; }

    /// <summary>Global rate limiting. Null = disabled.</summary>
    public RateLimitOptions? RateLimit { get; set; }

    /// <summary>CORS configuration. Null = no CORS.</summary>
    public CorsOptions? Cors { get; set; }

    /// <summary>Resilience configuration (circuit breaker, timeout). Null = use defaults.</summary>
    public Resilience.GatewayResilienceOptions? Resilience { get; set; }

    /// <summary>Custom maintenance page path.</summary>
    public string? MaintenancePagePath { get; set; }

    /// <summary>Enable OpenTelemetry. Default: true.</summary>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>Enable response compression. Default: true.</summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>Graceful shutdown drain timeout. Default: 30s.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
