namespace Showcase.Host.Configuration;

/// <summary>
/// Diagnostic endpoint for inspecting runtime configuration and secrets.
/// Demonstrates: IConfigurationStore (tenant-scoped) + ISecretStore usage from a minimal API endpoint.
/// Available only in Development environment.
/// </summary>
public static class ConfigurationDiagnosticsEndpoint
{
    public static void MapConfigurationDiagnostics(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return;

        var group = app.MapGroup("/api/diag/config").WithTags("Diagnostics");

        // GET /api/diag/config/{key} — resolve a configuration value (tenant-aware)
        group.MapGet("/{key}", async (
            string key,
            IConfigurationStore store,
            ITenantContext tenant,
            CancellationToken ct) =>
        {
            // Try tenant-specific value first, then fall back to base
            string? value = null;
            if (tenant.IsResolved)
                value = await store.GetAsync(key, tenant.TenantId!, ct).ConfigureAwait(false);

            value ??= await store.GetAsync(key, ct).ConfigureAwait(false);

            return value is not null
                ? Results.Ok(new { Key = key, Value = value, TenantId = tenant.TenantId })
                : Results.NotFound(new { Key = key, TenantId = tenant.TenantId });
        });

        // GET /api/diag/config/secret/{key} — resolve a secret (masked)
        group.MapGet("/secret/{key}", async (
            string key,
            ISecretStore secrets,
            CancellationToken ct) =>
        {
            var value = await secrets.GetSecretAsync(key, ct).ConfigureAwait(false);

            return value is not null
                ? Results.Ok(new { Key = key, Value = $"{value[..Math.Min(8, value.Length)]}***" })
                : Results.NotFound(new { Key = key });
        });
    }
}
