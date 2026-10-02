using Microsoft.AspNetCore.Builder;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Temporal.AspNetCore.Steps;

/// <summary>
///     Adds the temporal context middleware (timezone detection) to the pipeline.
///     Order 100 — after the typical authentication steps so
///     <see cref="Detection.ClaimsTimeZoneStrategy" /> sees the authenticated user.
///     If your authentication step runs later than 100, register your own step with
///     a higher order calling <c>app.UsePragmaticTemporal()</c> instead.
/// </summary>
public sealed class TemporalContextStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
        => app.UsePragmaticTemporal();
}
