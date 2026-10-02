using Microsoft.AspNetCore.Builder;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     Adds routing middleware to the pipeline.
///     Order 50 — runs after compression but before authentication/authorization.
/// </summary>
public class RoutingStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 50;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
        => app.UseRouting();
}
