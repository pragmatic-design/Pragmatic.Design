namespace Pragmatic.Composition.Remote;

/// <summary>
///     Abstraction for serializing/deserializing remote boundary payloads.
///     JSON is the default. Binary formats (MessagePack, MemoryPack) can be
///     registered as alternatives for internal cross-boundary communication.
/// </summary>
public interface IRemoteSerializer
{
    /// <summary>The MIME content type (e.g., "application/json", "application/x-msgpack").</summary>
    string ContentType { get; }

    /// <summary>Serializes a value to bytes.</summary>
    /// <remarks>
    ///     Returns a heap-allocated <see cref="byte"/> array by design. Remote-boundary serialization
    ///     happens once per RPC call and the result is immediately written to a network stream — the
    ///     allocation is negligible against the network round-trip, and a plain <c>byte[]</c> keeps
    ///     implementations (JSON, MessagePack, MemoryPack) trivial. A zero-alloc
    ///     <see cref="System.Buffers.IBufferWriter{T}"/> overload could be added later if profiling ever
    ///     shows this boundary on a hot path; it is intentionally NOT the default so every implementer
    ///     stays simple for a non-hot-path concern.
    /// </remarks>
    byte[] Serialize<T>(T value);

    /// <summary>Deserializes from a stream.</summary>
    ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default);
}
