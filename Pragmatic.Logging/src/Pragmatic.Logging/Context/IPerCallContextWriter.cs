namespace Pragmatic.Logging.Context;

/// <summary>
///     A per-call context provider that writes its properties straight into the entry, so that the
///     logging path does not build a dictionary for it on every call.
/// </summary>
internal interface IPerCallContextWriter
{
    /// <summary>Adds each property that <paramref name="include" /> accepts to <paramref name="target" />.</summary>
    void WriteContextProperties(IDictionary<string, object?> target, Func<string, bool> include);
}
