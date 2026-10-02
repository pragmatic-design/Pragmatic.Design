namespace Showcase.Host.AgentDemo;

/// <summary>
///     Demo endpoints that showcase Pragmatic Agent integration.
///     Each endpoint demonstrates a specific Agent/Gateway capability.
/// </summary>
public static class AgentDemoEndpoints
{
    public static void MapAgentDemoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/agent-demo").WithTags("Agent Demo");

        // 1. Config Push — reads config from Agent KV, changes in real-time via CLI
        group.MapGet("/config/{key}", async (string key, IConfigurationStore configStore) =>
        {
            var value = await configStore.GetAsync(key).ConfigureAwait(false);
            return value is not null
                ? Results.Ok(new { key, value, source = "agent-kv" })
                : Results.NotFound(new { key, message = "Key not found in Agent KV" });
        }).WithSummary("Read config from Agent KV (change via: pragmatic config set <key> <value>)");

        // 2. Config Push (tenant-scoped)
        group.MapGet("/config/{key}/tenant/{tenantId}", async (string key, string tenantId, IConfigurationStore configStore) =>
        {
            var value = await configStore.GetAsync(key, tenantId).ConfigureAwait(false);
            return value is not null
                ? Results.Ok(new { key, tenantId, value, source = "agent-kv" })
                : Results.NotFound(new { key, tenantId, message = "Key not found" });
        }).WithSummary("Read tenant-scoped config from Agent KV");

        // 3. Feature Flags — behavior changes based on flag
        group.MapGet("/feature/{flagName}", async (string flagName, IFeatureFlagStore flagStore) =>
        {
            var enabled = await flagStore.IsEnabledAsync(flagName).ConfigureAwait(false);
            var definition = await flagStore.GetDefinitionAsync(flagName).ConfigureAwait(false);

            return Results.Ok(new
            {
                flag = flagName,
                enabled,
                definition = definition is not null ? new { definition.Name, definition.Enabled } : null,
                message = enabled
                    ? $"Feature '{flagName}' is ENABLED"
                    : $"Feature '{flagName}' is DISABLED (change via: pragmatic flag set {flagName} true)"
            });
        }).WithSummary("Check feature flag from Agent KV");

        // 4. Feature Flag — conditional behavior
        group.MapGet("/greeting", async (IFeatureFlagStore flagStore) =>
        {
            var fancyGreeting = await flagStore.IsEnabledAsync("fancy-greeting").ConfigureAwait(false);
            var message = fancyGreeting
                ? "Welcome to Pragmatic Showcase Hotel & Resort — Where Every Stay is Extraordinary!"
                : "Welcome to Showcase Hotel.";

            return Results.Ok(new { message, fancyGreetingEnabled = fancyGreeting });
        }).WithSummary("Greeting changes based on 'fancy-greeting' feature flag");

        // 5. Dynamic Tenant — list tenants from Agent KV
        group.MapGet("/tenants", async (ITenantStore tenantStore) =>
        {
            var tenants = await tenantStore.GetAllAsync().ConfigureAwait(false);
            return Results.Ok(new
            {
                count = tenants.Count,
                tenants = tenants.Select(t => new
                {
                    t.TenantId,
                    t.TenantName,
                    state = t.State.ToString(),
                    hasConnectionString = t.ConnectionString is not null
                }),
                message = "Add tenant via: pragmatic tenant add <name> --plan starter"
            });
        }).WithSummary("List tenants from Agent KV (dynamic, no restart needed)");

        // 6. Health — Agent connectivity
        group.MapGet("/health", (Pragmatic.Agent.Client.AgentConnection agent) =>
        {
            return Results.Ok(new
            {
                agentConnected = agent.IsConnected,
                timestamp = DateTimeOffset.UtcNow,
                message = agent.IsConnected
                    ? "Agent connected — all stores backed by Agent KV"
                    : "Agent disconnected — running in L0 mode (NoOp fallback)"
            });
        }).WithSummary("Agent connectivity status");

        // 7. Secret — read a secret from encrypted KV
        group.MapGet("/secret/{key}", async (string key, IConfigurationStore configStore) =>
        {
            // Secrets are stored under secret/ prefix in Agent KV (encrypted at rest)
            var value = await configStore.GetAsync($"secret/{key}").ConfigureAwait(false);
            return value is not null
                ? Results.Ok(new { key, value = "***" + value[^4..], message = "Secret read from encrypted Agent KV" })
                : Results.NotFound(new { key, message = "Secret not found (set via: pragmatic config set secret/<key> <value>)" });
        }).WithSummary("Read secret from Agent KV (encrypted at rest with AES-256-GCM)");
    }
}
