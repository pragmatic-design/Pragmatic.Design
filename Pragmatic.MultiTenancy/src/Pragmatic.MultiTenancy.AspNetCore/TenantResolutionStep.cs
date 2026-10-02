using Microsoft.AspNetCore.Builder;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.MultiTenancy;

/// <summary>
///     Adds <see cref="TenantResolutionMiddleware" /> to the pipeline.
///     Order 92 — after <c>AuthenticationStep</c> (91), because the claim resolver reads the
///     authenticated principal, and before <c>InternationalizationStep</c> (95).
/// </summary>
/// <remarks>
///     A resolver registered without this step leaves the pipeline untouched, so the middleware
///     never runs and no tenant is ever resolved. Nothing fails: entities are written with an empty
///     tenant id and then match nothing on read — no exception, no log, no diagnostic. In a
///     framework whose whole point is isolating tenants from each other, that is the worst shape a
///     defect can take, which is why <c>AddPragmaticMultiTenancy</c> — the call behind
///     <c>UseMultiTenancy(...)</c> and the generated default — registers this step with the resolver.
/// </remarks>
public sealed class TenantResolutionStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 92;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();
}
