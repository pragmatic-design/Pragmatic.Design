using Microsoft.Extensions.Logging;
using Pragmatic.Endpoints.Context;

namespace Showcase.Booking.Infrastructure.Processors;

/// <summary>
///     Logs request details before the endpoint handler runs.
///     Demonstrates: <see cref="IEndpointPreProcessor" /> with [PreProcessor&lt;T&gt;] attribute.
/// </summary>
public class RequestLoggingPreProcessor(
    ILogger<RequestLoggingPreProcessor> logger) : IEndpointPreProcessor
{
    public ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct = default)
    {
        if (context is EndpointContext ctx)
        {
            logger.LogInformation(
                "Request starting: {Method} {Path} → {EndpointName}",
                ctx.Request.Method,
                ctx.Request.Path,
                ctx.EndpointName);
        }

        return ValueTask.FromResult(PreProcessorResult.Continue());
    }
}
