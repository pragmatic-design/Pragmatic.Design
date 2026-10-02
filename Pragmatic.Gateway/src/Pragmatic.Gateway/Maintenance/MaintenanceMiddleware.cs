namespace Pragmatic.Gateway.Maintenance;

/// <summary>
///     Early maintenance gate: serves a 503 for <b>full-gateway</b> maintenance
///     (<see cref="MaintenanceState.IsFullMaintenance"/>) so the Gateway stays up while everything behind
///     it is down. <b>Per-app</b> maintenance is handled later, inside the reverse-proxy pipeline (GW-M3),
///     where the matched route — and therefore its target app — is known, so only the affected app gets
///     503 instead of the whole gateway.
/// </summary>
internal sealed class MaintenanceMiddleware(
    RequestDelegate next,
    MaintenanceState state,
    MaintenancePage page)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Never gate the health endpoint: an orchestrator liveness probe getting 503 during
        // maintenance would kill the very gateway that is supposed to stay up while apps are down.
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Full gateway maintenance — every request gets 503 here, before auth. Per-app maintenance is
        // NOT decided here (the target app is unknown until routing) — it is decided in the proxy pipeline.
        if (state.IsFullMaintenance)
        {
            await page.WriteAsync(context).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
