namespace Pragmatic.Logging.Benchmarks.Json.Probe;

/// <summary>What the classic probe provider does differently from the JSON provider's classic path.</summary>
[Flags]
internal enum ClassicJsonProbeVariant
{
    /// <summary>The JSON provider's classic path, line for line.</summary>
    Same = 0,

    /// <summary>
    ///     One <c>Utf8JsonWriter</c> and one buffer kept by the provider and reset per line, instead of a new
    ///     <c>MemoryStream</c> and a new writer, which rents its own 4 KB of buffer from the stream.
    /// </summary>
    ReusedWriter = 1 << 0,

    /// <summary>
    ///     The line's bytes written to the stream as they are, instead of copied to an array, decoded into a
    ///     <see cref="string" /> and encoded again by the <c>TextWriter</c>.
    /// </summary>
    BytesOut = 1 << 1,

    /// <summary>The timestamp formatted into a stack buffer, instead of into a <see cref="string" />.</summary>
    StackTimestamp = 1 << 2,

    /// <summary>Nothing written at all: what the call costs before the provider writes anything.</summary>
    EmptyWrite = 1 << 3,
}
