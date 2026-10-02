namespace Pragmatic.Messaging;

/// <summary>
///     Marker implemented by SG-generated handler pipelines (<c>{Handler}.Pipeline</c>) that are
///     registered in DI as <see cref="IMessageHandler{T}"/> in place of the raw handler.
/// </summary>
/// <remarks>
///     The bus must treat a wrapped handler differently in two ways: it must not re-apply the
///     middleware chain (the pipeline already weaves middleware, idempotency, retry and telemetry
///     around the handler), and <c>[OnBus]</c> routing must match on the wrapped handler's type
///     name — the wrapper's own runtime type is a nested <c>Pipeline</c> class that never appears
///     in the SG-generated bus map.
/// </remarks>
public interface IPipelineWrappedHandler
{
    /// <summary>Fully qualified name (Namespace.TypeName) of the user handler wrapped by this pipeline.</summary>
    string HandlerTypeName { get; }
}
