namespace Pragmatic.Logging.Context;

/// <summary>
///     A per-call context provider that writes its properties straight into the entry, so that the
///     logging path does not build a dictionary for it on every call.
/// </summary>
/// <remarks>
///     It names its properties up front, so a logging provider decides which of them its filter accepts once,
///     rather than asking the filter for each name on every call.
/// </remarks>
internal interface IPerCallContextWriter
{
    /// <summary>The properties the provider can write, at most 64, in a fixed order.</summary>
    IReadOnlyList<string> PropertyNames { get; }

    /// <summary>
    ///     Adds to <paramref name="target" /> each property whose bit in <paramref name="included" /> is set, bit
    ///     <c>i</c> standing for <see cref="PropertyNames" />[<c>i</c>].
    /// </summary>
    void WriteContextProperties(IDictionary<string, object?> target, ulong included);
}
