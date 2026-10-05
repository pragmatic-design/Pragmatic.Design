namespace Pragmatic.Logging.Context;

/// <summary>
///     One provider's place in a merge of context properties.
/// </summary>
/// <param name="Provider">The provider.</param>
/// <param name="StaticProperties">
///     The properties of a static provider, read once. Null for a per-call provider, which is read when the
///     merge happens.
/// </param>
/// <remarks>
///     Layers come highest <see cref="IContextProvider.Priority" /> value first. A merge that writes each
///     layer over the previous one therefore leaves the lowest value's property in place, which is the
///     documented rule, whether the providers are static or per call.
/// </remarks>
internal sealed record ContextLayer(IContextProvider Provider, KeyValuePair<string, object?>[]? StaticProperties);
