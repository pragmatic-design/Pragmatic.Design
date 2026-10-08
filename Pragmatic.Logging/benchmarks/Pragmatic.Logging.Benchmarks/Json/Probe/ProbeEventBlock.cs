namespace Pragmatic.Logging.Benchmarks.Json.Probe;

/// <summary>
///     The event block of one call site's state type, encoded once: a static field per <typeparamref name="TState" />,
///     read with no lookup, as <c>Utf8LogStateWriters&lt;TState&gt;</c> is.
/// </summary>
internal static class ProbeEventBlock<TState>
{
    /// <summary><c>,"@eventId":…,"@eventName":…,"@messageTemplate":…</c> in UTF-8, or null before the first call.</summary>
    public static byte[]? Bytes;

    /// <summary>The event the bytes were encoded for; another event id on the same state is written field by field.</summary>
    public static int Id;

    /// <summary>The event name the bytes were encoded for, compared by reference.</summary>
    public static string? Name;
}
