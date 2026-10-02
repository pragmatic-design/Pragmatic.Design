// Pragmatic Gateway — enterprise YARP API Gateway with Agent integration

using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Pragmatic.Agent.Client;
using Pragmatic.Gateway;
using Pragmatic.Gateway.Health;
using Pragmatic.Gateway.Maintenance;
using Pragmatic.Gateway.Middleware;
using Pragmatic.Gateway.Resilience;
using Pragmatic.Gateway.Routing;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// ═══ Configuration ═══
var options = builder.Configuration.GetSection("Gateway").Get<GatewayOptions>() ?? new GatewayOptions();

// CLI arg overrides
var agentSocketExplicit = false;
for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--agent-socket": options.AgentSocketPath = args[i + 1]; agentSocketExplicit = true; break;
        case "--listen": options.HttpUrl = args[i + 1]; break;
        case "--https": options.HttpsUrl = args[i + 1]; break;
        case "--maintenance-page": options.MaintenancePagePath = args[i + 1]; break;
    }
}

var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Gateway");
logger.LogInformation("Pragmatic Gateway v{Version}", version);
logger.LogInformation("HTTP: {HttpUrl}, HTTPS: {HttpsUrl}", options.HttpUrl, options.HttpsUrl ?? "disabled");

// ═══ Agent Connection ═══
// Precedence: an explicit --agent-socket wins; otherwise a configured AgentInstance resolves the
// instance-scoped socket (shared convention via AgentSocketPath, so the Gateway and the daemon agree);
// otherwise the default socket.
var agentSocketPath = agentSocketExplicit
    ? options.AgentSocketPath
    : options.AgentInstance is { Length: > 0 } agentInstance
        ? AgentSocketPath.Resolve(agentInstance)
        : options.AgentSocketPath;
var agentConnection = new AgentConnection(agentSocketPath);
builder.Services.AddSingleton(agentConnection);
builder.Services.AddSingleton(options);

// ═══ Telemetry (OpenTelemetry) ═══
// Wires GatewayOptions.EnableTelemetry (default true): emit distributed traces + metrics for the
// gateway and its proxied HttpClient calls, plus YARP's own meters. The exporter targets the OTLP
// collector configured via the standard OTEL_EXPORTER_OTLP_ENDPOINT env var (default localhost:4317).
if (options.EnableTelemetry)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("pragmatic-gateway", serviceVersion: version))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter())
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("Yarp.ReverseProxy")
            .AddOtlpExporter());
}

// ═══ YARP Reverse Proxy ═══
// Register the Agent-backed provider as the IProxyConfigProvider YARP actually consumes.
// Registering it only as the concrete type (with LoadFromMemory as the sole provider) left YARP
// serving an empty in-memory config — the dynamic + static routes were loaded but never routed.
builder.Services.AddSingleton<AgentRouteProvider>();
builder.Services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<AgentRouteProvider>());
builder.Services.AddReverseProxy()
    .AddTransforms(transforms =>
    {
        // Forward the configured JWT claims to the backend as X-Claim-{type} headers. The gateway is the
        // trust boundary: strip any client-supplied copy first, then set the authoritative value from the
        // validated token, so a backend may trust X-Claim-* exactly when it is only reachable via the
        // gateway. Inert unless JWT auth is configured with a non-empty ForwardClaims list.
        var forwardClaims = options.Jwt?.ForwardClaims;
        if (forwardClaims is not { Count: > 0 })
            return;

        transforms.AddRequestTransform(request =>
        {
            ClaimForwarding.Apply(forwardClaims, request.HttpContext.User, request.ProxyRequest.Headers);
            return ValueTask.CompletedTask;
        });
    });

// ═══ Maintenance State ═══
var maintenanceState = new MaintenanceState();
maintenanceState.SubscribeToAgent(agentConnection);
builder.Services.AddSingleton(maintenanceState);
var maintenancePage = new MaintenancePage(options);
builder.Services.AddSingleton(maintenancePage);

// ═══ Resilience (circuit breaker + timeout per cluster) ═══
builder.Services.AddGatewayResilience(options.Resilience ?? new GatewayResilienceOptions());

// ═══ Health Checks ═══
builder.Services.AddHealthChecks()
    .AddCheck<GatewayHealthCheck>("gateway");

// ═══ Response Compression ═══
if (options.EnableCompression)
{
    builder.Services.AddResponseCompression(o =>
    {
        o.EnableForHttps = true;
    });
}

// ═══ CORS ═══
if (options.Cors is not null)
{
    // Wildcard origin + credentials is an invalid CORS combination: the browser refuses it
    // and ASP.NET Core throws an opaque exception when the policy is applied. Fail fast at
    // startup with an actionable message instead.
    if (options.Cors.Origins.Contains("*") && options.Cors.AllowCredentials)
    {
        throw new InvalidOperationException(
            "Invalid CORS configuration: AllowCredentials cannot be combined with a wildcard ('*') " +
            "origin. List explicit origins, or disable AllowCredentials.");
    }

    builder.Services.AddCors(cors =>
    {
        cors.AddDefaultPolicy(policy =>
        {
            if (options.Cors.Origins.Contains("*"))
                policy.AllowAnyOrigin();
            else
                policy.WithOrigins(options.Cors.Origins.ToArray());

            policy.WithMethods(options.Cors.Methods.ToArray());

            if (options.Cors.Headers.Contains("*"))
                policy.AllowAnyHeader();
            else
                policy.WithHeaders(options.Cors.Headers.ToArray());

            if (options.Cors.AllowCredentials)
                policy.AllowCredentials();
        });
    });
}

// ═══ Authentication (JWT) ═══
if (options.Jwt is not null)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(jwt =>
        {
            var tvp = new TokenValidationParameters
            {
                // Always require a valid signature regardless of whether a symmetric key
                // or JWKS is used. Without this a token with no signature (alg=none)
                // would be accepted when falling back to JWKS-only validation.
                ValidateIssuerSigningKey = true,

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),

                // Issuer/audience: validate when configured; warn at startup when absent
                // to encourage callers to lock these down in production.
                ValidateIssuer = options.Jwt.Issuer is not null,
                ValidIssuer = options.Jwt.Issuer,
                ValidateAudience = options.Jwt.Audience is not null,
                ValidAudience = options.Jwt.Audience
            };

            if (options.Jwt.Issuer is null)
                logger.LogWarning("JWT issuer validation is disabled. Set Gateway:Jwt:Issuer to prevent token forgery across issuers.");

            if (options.Jwt.Audience is null)
                logger.LogWarning("JWT audience validation is disabled. Set Gateway:Jwt:Audience to prevent token reuse across services.");

            if (options.Jwt.SigningKey is not null)
            {
                tvp.IssuerSigningKey =
                    new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(options.Jwt.SigningKey));
            }

            jwt.TokenValidationParameters = tvp;

            if (options.Jwt.JwksUrl is not null)
            {
                jwt.MetadataAddress = options.Jwt.JwksUrl;
            }
        });

    builder.Services.AddAuthorization();
}

// ═══ Rate Limiting ═══
if (options.RateLimit is not null)
{
    builder.Services.AddRateLimiter(limiter =>
    {
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        var permitLimit = options.RateLimit.PermitLimit;
        var window = options.RateLimit.Window;
        var byTenant = string.Equals(options.RateLimit.KeyStrategy, "tenant", StringComparison.OrdinalIgnoreCase);

        // GlobalLimiter applies to EVERY request — including YARP-proxied traffic — so the limit is
        // actually enforced. A named limiter alone was inert because no route called RequireRateLimiting.
        limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var partitionKey = byTenant
                ? (context.Request.Headers["X-Tenant-Id"].ToString() is { Length: > 0 } tenant ? tenant : "unknown")
                : (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            // Per-route override (RouteEntry.RateLimit, requests/minute). Endpoint routing has already run
            // by the time the limiter executes, so the matched YARP route — and its metadata — is available.
            // A route that declares its own limit is capped tighter than the global default, partitioned
            // by (route, base key) so one route's budget never spills into another's.
            var route = context.GetEndpoint()?.Metadata.GetMetadata<RouteModel>()?.Config;
            if (route?.Metadata is { } routeMetadata
                && routeMetadata.TryGetValue(GatewayRouteMetadata.RateLimit, out var perRouteRaw)
                && int.TryParse(perRouteRaw, out var perRoute) && perRoute > 0)
            {
                return RateLimitPartition.GetFixedWindowLimiter($"route:{route.RouteId}:{partitionKey}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = perRoute,
                        Window = TimeSpan.FromMinutes(1),
                    });
            }

            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
            });
        });
    });
}

// ═══ Kestrel / URLs ═══
var urls = new List<string> { options.HttpUrl };

if (options.HttpsUrl is not null)
{
    urls.Add(options.HttpsUrl);

    if (options.Tls.CertificatePath is not null)
    {
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ConfigureHttpsDefaults(https =>
            {
                https.ServerCertificate = options.Tls.KeyPath is not null
                    ? X509Certificate2.CreateFromPemFile(options.Tls.CertificatePath, options.Tls.KeyPath)
                    : X509CertificateLoader.LoadPkcs12FromFile(options.Tls.CertificatePath, options.Tls.CertificatePassword);
            });
        });
    }
}

builder.WebHost.UseUrls(urls.ToArray());

// ═══ Graceful Shutdown ═══
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = options.ShutdownTimeout);

var app = builder.Build();

// ═══ Connect to Agent (best-effort) ═══
var routeProvider = app.Services.GetRequiredService<AgentRouteProvider>();
try
{
    await agentConnection.ConnectAsync().ConfigureAwait(false);
    await agentConnection.RegisterAsync("gateway", "Pragmatic Gateway").ConfigureAwait(false);
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Agent unavailable — serving static routes from config only ({Count} routes)", options.Routes.Count);
}

// Load routes regardless of Agent connectivity: ReloadAsync merges Agent KV routes (only when
// connected) with the static fallback from config, so static routes are served even when the Agent
// is down. Calling it only inside the try left the empty ctor snapshot in place on the failure path
// → the gateway 404'd everything despite the "using static routes" log.
await routeProvider.ReloadAsync().ConfigureAwait(false);
logger.LogInformation("{RouteCount} routes loaded (agent connected: {Connected})",
    routeProvider.RouteCount, agentConnection.IsConnected);

// ═══ Middleware Pipeline (order matters) ═══

// 1. Health check (always available, even during maintenance)
app.MapHealthChecks("/health");

// 2. Response compression
if (options.EnableCompression)
    app.UseResponseCompression();

// 3. CORS
if (options.Cors is not null)
    app.UseCors();

// 4. Maintenance check (before auth — no point authenticating during maintenance)
app.UseMiddleware<MaintenanceMiddleware>();

// 5. API key gate (when Gateway:ApiKey is configured) — rejects requests without a valid key
if (options.ApiKey is not null)
    app.UseMiddleware<ApiKeyMiddleware>(options.ApiKey);

// 6. Authentication + Authorization
if (options.Jwt is not null)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// 7. Tenant resolution — MUST run after authentication (so the signed tenant claim wins) and
//    BEFORE rate limiting. The rate limiter partitions on X-Tenant-Id; if it ran first it would
//    key on the raw client-supplied header (auth + tenant stripping not applied yet), letting an
//    attacker rotate the header for unlimited partitions or target a victim tenant's budget.
app.UseMiddleware<TenantRoutingMiddleware>();

// 8. Rate limiting — partitions on the gateway-authoritative X-Tenant-Id set just above.
if (options.RateLimit is not null)
    app.UseRateLimiter();

// 8. YARP reverse proxy with resilience (circuit breaker + timeout)
app.MapReverseProxy(proxyPipeline =>
{
    // GW-M3: per-app maintenance. The matched route is known here, so 503 ONLY when this route's
    // target app is in maintenance — the rest of the gateway keeps serving. Full-gateway maintenance
    // was already short-circuited earlier by MaintenanceMiddleware.
    proxyPipeline.Use(async (context, nextP) =>
    {
        var route = context.GetReverseProxyFeature().Route;
        if (route.Config.Metadata is { } metadata
            && metadata.TryGetValue(GatewayRouteMetadata.AppId, out var appId)
            && maintenanceState.IsInMaintenance(appId))
        {
            await maintenancePage.WriteAsync(context).ConfigureAwait(false);
            return;
        }

        await nextP().ConfigureAwait(false);
    });

    // YARP's own intermediate steps, in its default order. A custom pipeline gets only the first and the
    // last step, so without these a cluster's LoadBalancingPolicy, session affinity and passive health
    // checks were configured and never run: the final step picked a destination at random.
    proxyPipeline.UseSessionAffinity();
    proxyPipeline.UseLoadBalancing();
    proxyPipeline.UsePassiveHealthChecks();

    proxyPipeline.UseGatewayResilience();
});

logger.LogInformation("Gateway ready");
await app.RunAsync().ConfigureAwait(false);
