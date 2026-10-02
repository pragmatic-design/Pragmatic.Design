using Microsoft.AspNetCore.Builder;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     Adds response compression middleware to the pipeline.
///     Order 25 — runs early to compress all subsequent responses.
/// </summary>
public class ResponseCompressionStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 25;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
        => app.UseResponseCompression();
}
