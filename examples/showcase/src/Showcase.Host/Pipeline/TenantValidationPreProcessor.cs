using Pragmatic.Endpoints.Context;
using Pragmatic.Result.Http;

namespace Showcase.Host.Pipeline;

/// <summary>
/// Validates that a tenant has been resolved before processing the request.
/// Demonstrates: IEndpointPreProcessor — runs before HandleAsync.
/// </summary>
public class TenantValidationPreProcessor(
    ITenantContext tenantContext,
    ILogger<TenantValidationPreProcessor> logger) : IEndpointPreProcessor
{
    public ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct = default)
    {
        if (!tenantContext.IsResolved)
        {
            if (context is EndpointContext ctx)
            {
                logger.LogWarning(
                    "Tenant not resolved for {Method} {Path}",
                    ctx.Request.Method,
                    ctx.Request.Path);
            }

            return ValueTask.FromResult(
                PreProcessorResult.Fail(UnauthorizedError.Create("TenantNotResolved")));
        }

        if (context is EndpointContext resolvedCtx)
        {
            logger.LogDebug(
                "Tenant {TenantId} resolved for {Method} {Path}",
                tenantContext.TenantId,
                resolvedCtx.Request.Method,
                resolvedCtx.Request.Path);
        }

        return ValueTask.FromResult(PreProcessorResult.Continue());
    }
}
