namespace Pragmatic.Gateway.Samples;

/// <summary>
///     Builds a complete <see cref="GatewayOptions" /> graph in code — the same model the Gateway
///     binds from <c>pragmatic-gateway.yaml</c> / appsettings.json / env vars / CLI args.
///     Demonstrates listen URLs, static fallback routes, and every feature sub-section.
/// </summary>
internal static class GatewayOptionsSample
{
    public static void Run()
    {
        SampleConsole.Header("GatewayOptions — full configuration model");

        // The whole gateway is configured through this single options object. In a real host it is
        // populated via: builder.Configuration.GetSection("Gateway").Get<GatewayOptions>().
        var options = new GatewayOptions
        {
            HttpUrl = "http://*:8080",
            HttpsUrl = "https://*:8443",
            AgentInstance = "gateway-01",

            // Static routes act as a FALLBACK that is merged in whenever the Agent is unavailable
            // (or has not yet pushed a dynamic route with the same RouteId).
            Routes =
            [
                new RouteEntry
                {
                    RouteId = "api",
                    Path = "/api/{**catch-all}",
                    Backends = ["http://backend-api:5000"],
                    RequireAuth = true,
                    RateLimit = 600
                },
                new RouteEntry
                {
                    RouteId = "tenant-app",
                    Path = "/{**catch-all}",
                    Backends = ["http://app:3000"],
                    Host = "acme.example.com"
                }
            ],

            // Each of these is null-by-default = feature disabled. Set them to opt in.
            Jwt = new JwtOptions { Issuer = "https://auth.example.com", Audience = "gateway" },
            ApiKey = new ApiKeyOptions(),
            RateLimit = new RateLimitOptions { PermitLimit = 1_000, Window = TimeSpan.FromMinutes(1) },
            Cors = new CorsOptions { Origins = ["https://app.example.com"] },
            Resilience = new Resilience.GatewayResilienceOptions(),

            MaintenancePagePath = "/etc/pragmatic/maintenance.html",
            EnableTelemetry = true,
            EnableCompression = true,
            ShutdownTimeout = TimeSpan.FromSeconds(30)
        };

        SampleConsole.Section("Listeners & Agent");
        SampleConsole.Item("HttpUrl", options.HttpUrl);
        SampleConsole.Item("HttpsUrl", options.HttpsUrl ?? "(disabled)");
        SampleConsole.Item("AgentSocketPath", options.AgentSocketPath);
        SampleConsole.Item("AgentInstance", options.AgentInstance ?? "(default)");

        SampleConsole.Section("Static fallback routes");
        foreach (var route in options.Routes)
        {
            var auth = route.RequireAuth ? "auth" : "anon";
            var rl = route.RateLimit is { } limit ? $"{limit}/min" : "global";
            var host = route.Host is null ? "*" : route.Host;
            SampleConsole.Item(route.RouteId, $"{route.Path}  ->  {string.Join(", ", route.Backends)}  [{host}, {auth}, {rl}]");
        }

        SampleConsole.Section("Enabled features (null = disabled)");
        SampleConsole.Item("Jwt", options.Jwt is null ? "off" : "on");
        SampleConsole.Item("ApiKey", options.ApiKey is null ? "off" : "on");
        SampleConsole.Item("RateLimit", options.RateLimit is null ? "off" : "on");
        SampleConsole.Item("Cors", options.Cors is null ? "off" : "on");
        SampleConsole.Item("Resilience", options.Resilience is null ? "defaults" : "configured");

        SampleConsole.Section("Operational toggles");
        SampleConsole.Item("MaintenancePagePath", options.MaintenancePagePath ?? "(built-in default)");
        SampleConsole.Item("EnableTelemetry", options.EnableTelemetry);
        SampleConsole.Item("EnableCompression", options.EnableCompression);
        SampleConsole.Item("ShutdownTimeout", options.ShutdownTimeout);
    }
}
