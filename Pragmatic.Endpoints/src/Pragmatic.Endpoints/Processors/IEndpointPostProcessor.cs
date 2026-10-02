using System.Diagnostics;
using Pragmatic.Endpoints.Context;

namespace Pragmatic.Endpoints.Processors;

/// <summary>
///     Interface for endpoint post-processors that run after the handler.
/// </summary>
/// <remarks>
///     <para>
///         Post-processors can modify the response, log results,
///         or perform cleanup operations.
///     </para>
///     <para>
///         Post-processors run in the order they are declared on the endpoint.
///         They run regardless of whether the handler succeeded or failed.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public class AuditLogProcessor : IEndpointPostProcessor
/// {
///     private readonly IAuditService _audit;
///
///     public AuditLogProcessor(IAuditService audit)
///     {
///         _audit = audit;
///     }
///
///     public async ValueTask ProcessAsync(IEndpointContext context, object? result, CancellationToken ct)
///     {
///         await _audit.LogAsync(new AuditEntry
///         {
///             Endpoint = context.EndpointName,
///             User = context.User?.Identity?.Name,
///             Timestamp = DateTimeOffset.UtcNow,
///             Success = result is not IError
///         }, ct);
///     }
/// }
/// </code>
/// </example>
public interface IEndpointPostProcessor
{
    /// <summary>
    ///     Processes the response after the endpoint handler executes.
    /// </summary>
    /// <param name="context">The endpoint context.</param>
    /// <param name="result">The result from the handler (success or error).</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask ProcessAsync(IEndpointContext context, object? result, CancellationToken ct = default);
}

/// <summary>
///     Typed post-processor that has access to the endpoint instance.
/// </summary>
/// <typeparam name="TEndpoint">The endpoint type.</typeparam>
public interface IEndpointPostProcessor<TEndpoint> : IEndpointPostProcessor
    where TEndpoint : class
{
    // Default implementation delegates to typed version
    ValueTask IEndpointPostProcessor.ProcessAsync(IEndpointContext context, object? result, CancellationToken ct)
    {
        if (context.Endpoint is TEndpoint typed)
            return ProcessAsync(typed, context, result, ct);

        // Processor type mismatch: the endpoint instance is not TEndpoint.
        // This indicates a misconfigured processor registration.
        Debug.Fail($"[Pragmatic.Endpoints] {GetType().Name} registered on endpoint '{context.EndpointName}' " +
                   $"but endpoint type '{context.Endpoint?.GetType().Name}' is not '{typeof(TEndpoint).Name}'. " +
                   "Check processor registration.");

        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Processes the response after the endpoint handler executes.
    /// </summary>
    /// <param name="endpoint">The endpoint instance.</param>
    /// <param name="context">The endpoint context.</param>
    /// <param name="result">The result from the handler (success or error).</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask ProcessAsync(TEndpoint endpoint, IEndpointContext context, object? result, CancellationToken ct = default);
}
