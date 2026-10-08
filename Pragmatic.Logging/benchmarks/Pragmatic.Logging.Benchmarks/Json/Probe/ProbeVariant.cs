namespace Pragmatic.Logging.Benchmarks.Json.Probe;

/// <summary>What the probe provider leaves out of, or does differently from, the JSON provider's UTF-8 path.</summary>
[Flags]
internal enum ProbeVariant
{
    /// <summary>The JSON provider's path, line for line.</summary>
    Same = 0,

    /// <summary>No write lock.</summary>
    NoLock = 1 << 0,

    /// <summary>The line stays in the buffer: no stream writes and no flush.</summary>
    NoStream = 1 << 1,

    /// <summary>The timestamp written by <c>Utf8JsonWriter</c> from the <see cref="DateTime" />: another text, STJ's.</summary>
    BuiltInTimestamp = 1 << 2,

    /// <summary>No <c>@eventId</c>, <c>@eventName</c> or <c>@messageTemplate</c>.</summary>
    NoExtraFields = 1 << 3,

    /// <summary>The level and the category written from text encoded once.</summary>
    EncodedConstants = 1 << 4,

    /// <summary><c>AutoFlush</c> read once, not looked up per line.</summary>
    AutoFlushOnce = 1 << 5,

    /// <summary>
    ///     The timestamp format parsed by <c>DateTime.TryFormat</c> on every line, as the provider did before
    ///     <c>TimestampLayout</c>: the same text.
    /// </summary>
    ParsedEachLine = 1 << 6,

    /// <summary>The same <c>@eventName</c> and <c>@messageTemplate</c>, from text encoded once.</summary>
    EncodedEventFields = 1 << 7,

    /// <summary>Nothing written at all: what the call costs before the provider formats anything.</summary>
    EmptyWrite = 1 << 8,

    /// <summary>No <c>@messageTemplate</c>, the one field ZLogger's formatter cannot write.</summary>
    NoTemplate = 1 << 9,

    /// <summary>
    ///     The message rendered between quotes and written raw: no second pass to escape and copy it. An upper
    ///     bound only — a real writer still escapes the string arguments, which this skips.
    /// </summary>
    RawMessage = 1 << 10,

    /// <summary>
    ///     The same bytes, with the parts that do not change between calls copied as blocks encoded once:
    ///     <c>,"@level":…,"@logger":…</c> per logger and level, <c>,"@eventId":…,"@eventName":…,"@messageTemplate":…</c>
    ///     per call site.
    /// </summary>
    ConstantBlocks = 1 << 11,

    /// <summary>With <see cref="ConstantBlocks" />: only the call site's event block; level and logger as today.</summary>
    EventBlockOnly = 1 << 12,
}
