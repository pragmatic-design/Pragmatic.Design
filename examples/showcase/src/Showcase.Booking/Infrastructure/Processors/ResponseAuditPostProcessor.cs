using Microsoft.Extensions.Logging;
using Pragmatic.Endpoints.Context;

namespace Showcase.Booking.Infrastructure.Processors;

/// <summary>
///     Logs response details after the endpoint handler completes.
///     Demonstrates: <see cref="IEndpointPostProcessor" /> with [PostProcessor&lt;T&gt;] attribute.
/// </summary>
public class ResponseAuditPostProcessor(
    ILogger<ResponseAuditPostProcessor> logger) : IEndpointPostProcessor
{
    public ValueTask ProcessAsync(IEndpointContext context, object? result, CancellationToken ct = default)
    {
        if (context is EndpointContext ctx)
        {
            logger.LogInformation(
                "Request completed: {Method} {Path} → {StatusCode} ({EndpointName})",
                ctx.Request.Method,
                ctx.Request.Path,
                ctx.Response.StatusCode,
                ctx.EndpointName);
        }

        return ValueTask.CompletedTask;
    }
}
