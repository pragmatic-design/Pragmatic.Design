using Pragmatic.Actions.Boundary;

namespace Pragmatic.Actions.Samples.Boundaries;

/// <summary>
///     A remote bounded context marker for the Shipping domain.
///     Configured via <c>UseRemote(baseUrl)</c> so calls are dispatched over HTTP
///     to a separately deployed service instead of running in-process.
/// </summary>
public sealed class ShippingBoundary : IBoundary;
