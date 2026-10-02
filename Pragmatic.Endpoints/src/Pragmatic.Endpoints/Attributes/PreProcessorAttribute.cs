using Pragmatic.Endpoints.Processors;

namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Attaches a pre-processor to an endpoint that runs before the handler.
/// </summary>
/// <remarks>
///     <para>
///         Multiple pre-processors can be attached and they run in declaration order.
///         If any pre-processor returns an error, subsequent processors are skipped.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Post, "/api/orders")]
/// [PreProcessor&lt;ValidateCustomerProcessor&gt;]
/// [PreProcessor&lt;CheckInventoryProcessor&gt;]
/// public partial class PlaceOrder : DomainAction&lt;OrderId&gt; { }
/// </code>
/// </example>
/// <typeparam name="TProcessor">The pre-processor type implementing <see cref="IEndpointPreProcessor" />.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class PreProcessorAttribute<TProcessor> : Attribute
    where TProcessor : IEndpointPreProcessor
{
    /// <summary>
    ///     Gets or sets the order in which this processor runs.
    ///     Lower values run first. Default is 0.
    /// </summary>
    public int Order { get; set; }
}