using Pragmatic.Endpoints.Processors;

namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Attaches a post-processor to an endpoint that runs after the handler.
/// </summary>
/// <remarks>
///     <para>
///         Multiple post-processors can be attached and they run in declaration order.
///         Post-processors run regardless of whether the handler succeeded or failed.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Post, "/api/orders")]
/// [PostProcessor&lt;AuditLogProcessor&gt;]
/// [PostProcessor&lt;NotificationProcessor&gt;]
/// public partial class PlaceOrder : DomainAction&lt;OrderId&gt; { }
/// </code>
/// </example>
/// <typeparam name="TProcessor">The post-processor type implementing <see cref="IEndpointPostProcessor" />.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class PostProcessorAttribute<TProcessor> : Attribute
    where TProcessor : IEndpointPostProcessor
{
    /// <summary>
    ///     Gets or sets the order in which this processor runs.
    ///     Lower values run first. Default is 0.
    /// </summary>
    public int Order { get; set; }
}