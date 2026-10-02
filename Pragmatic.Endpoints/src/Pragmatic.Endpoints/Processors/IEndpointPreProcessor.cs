using System.Diagnostics;
using Pragmatic.Endpoints.Context;

namespace Pragmatic.Endpoints.Processors;

/// <summary>
///     Interface for endpoint pre-processors that run before the handler.
/// </summary>
/// <remarks>
///     <para>
///         Pre-processors can modify the request context, perform validation,
///         or short-circuit the request by returning an error result.
///     </para>
///     <para>
///         Pre-processors run in the order they are declared on the endpoint.
///         If a pre-processor returns an error, subsequent processors and
///         the handler are not executed.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public class ValidateCustomerProcessor : IEndpointPreProcessor
/// {
///     private readonly ICustomerService _customers;
///
///     public ValidateCustomerProcessor(ICustomerService customers)
///     {
///         _customers = customers;
///     }
///
///     public async ValueTask&lt;PreProcessorResult&gt; ProcessAsync(IEndpointContext context, CancellationToken ct)
///     {
///         var customerId = context.GetRouteValue&lt;Guid&gt;("customerId");
///         var exists = await _customers.ExistsAsync(customerId, ct);
///
///         if (!exists)
///             return PreProcessorResult.NotFound("Customer", customerId.ToString());
///
///         return PreProcessorResult.Continue();
///     }
/// }
/// </code>
/// </example>
public interface IEndpointPreProcessor
{
    /// <summary>
    ///     Processes the request before the endpoint handler executes.
    /// </summary>
    /// <param name="context">The endpoint context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result indicating whether to continue or short-circuit.</returns>
    ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct = default);
}

/// <summary>
///     Typed pre-processor that has access to the endpoint instance.
/// </summary>
/// <typeparam name="TEndpoint">The endpoint type.</typeparam>
public interface IEndpointPreProcessor<TEndpoint> : IEndpointPreProcessor
    where TEndpoint : class
{
    // Default implementation delegates to typed version
    ValueTask<PreProcessorResult> IEndpointPreProcessor.ProcessAsync(IEndpointContext context, CancellationToken ct)
    {
        if (context.Endpoint is TEndpoint typed)
            return ProcessAsync(typed, context, ct);

        // Processor type mismatch: the endpoint instance is not TEndpoint.
        // This indicates a misconfigured processor registration.
        Debug.Fail($"[Pragmatic.Endpoints] {GetType().Name} registered on endpoint '{context.EndpointName}' " +
                   $"but endpoint type '{context.Endpoint?.GetType().Name}' is not '{typeof(TEndpoint).Name}'. " +
                   "Check processor registration.");

        return ValueTask.FromResult(PreProcessorResult.Continue());
    }

    /// <summary>
    ///     Processes the request before the endpoint handler executes.
    /// </summary>
    /// <param name="endpoint">The endpoint instance.</param>
    /// <param name="context">The endpoint context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result indicating whether to continue or short-circuit.</returns>
    ValueTask<PreProcessorResult> ProcessAsync(TEndpoint endpoint, IEndpointContext context,
        CancellationToken ct = default);
}