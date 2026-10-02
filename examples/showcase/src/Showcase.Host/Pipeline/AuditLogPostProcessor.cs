using Pragmatic.Endpoints.Context;

namespace Showcase.Host.Pipeline;

/// <summary>
/// Logs endpoint invocations for audit trail.
/// Demonstrates: IEndpointPostProcessor — runs after every endpoint HandleAsync.
/// </summary>
public class AuditLogPostProcessor(ILogger<AuditLogPostProcessor> logger) : IEndpointPostProcessor
{
    public ValueTask ProcessAsync(IEndpointContext context, object? result, CancellationToken ct = default)
    {
        if (context is EndpointContext ctx)
        {
            logger.LogInformation(
                "Endpoint {EndpointName} completed: {Method} {Path} → {StatusCode}",
                ctx.EndpointName,
                ctx.Request.Method,
                ctx.Request.Path,
                ctx.Response.StatusCode);
        }

        return ValueTask.CompletedTask;
    }
}
