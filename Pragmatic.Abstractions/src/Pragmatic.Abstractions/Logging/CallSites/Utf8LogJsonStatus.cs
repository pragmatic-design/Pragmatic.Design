namespace Pragmatic.Logging.CallSites;

/// <summary>What <see cref="IUtf8LogState.TryFormatMessageAndJson" /> did.</summary>
public enum Utf8LogJsonStatus
{
    /// <summary>The message and the properties' JSON were written.</summary>
    Written,

    /// <summary>A buffer was too small; nothing in either is to be relied on. A larger one may succeed.</summary>
    BufferTooSmall,

    /// <summary>
    ///     The properties are for a writer to write: the call site writes them only through
    ///     <see cref="IUtf8LogState.WriteProperties" />, or one of this call's values needs escaping.
    /// </summary>
    NeedsWriter,
}
