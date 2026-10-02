using System.Buffers;
using System.Buffers.Binary;
using Pragmatic.Agent.Protocol.Serialization;

namespace Pragmatic.Agent.Protocol;

/// <summary>
///     Encodes/decodes length-prefixed frames for socket communication.
///     Wire format: [4-byte big-endian length][serialized payload].
///     Serialization format is pluggable via <see cref="IWireFormat"/> (default: JSON).
/// </summary>
public static class FrameCodec
{
    /// <summary>
    ///     Default wire format (JSON). Immutable global: a mutable static was racy under
    ///     concurrent multi-tenant use. To use a binary format (e.g. MessagePack), pass the
    ///     <c>format</c> argument per call/connection rather than mutating global state.
    /// </summary>
    public static IWireFormat DefaultFormat => JsonWireFormat.Instance;

    private const int MaxFrameSize = 16 * 1024 * 1024; // 16MB

    /// <summary>Encodes a message into a length-prefixed frame.</summary>
    public static byte[] Encode(AgentMessage message, IWireFormat? format = null)
    {
        var serialized = (format ?? DefaultFormat).Serialize(message);
        var frame = new byte[4 + serialized.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), serialized.Length);
        serialized.CopyTo(frame.AsSpan(4));
        return frame;
    }

    /// <summary>Tries to decode a complete frame from the buffer.</summary>
    public static (AgentMessage? Message, int BytesConsumed) TryDecode(ReadOnlySpan<byte> buffer, IWireFormat? format = null)
    {
        if (buffer.Length < 4)
            return (null, 0);

        var length = BinaryPrimitives.ReadInt32BigEndian(buffer[..4]);

        if (length <= 0 || length > MaxFrameSize)
            throw new InvalidOperationException($"Invalid frame length: {length}");

        if (buffer.Length < 4 + length)
            return (null, 0);

        var payload = buffer.Slice(4, length);
        var message = (format ?? DefaultFormat).Deserialize<AgentMessage>(payload);

        return (message, 4 + length);
    }

    /// <summary>
    ///     Reads a complete frame from a Stream asynchronously.
    /// </summary>
    /// <remarks>
    ///     Cancellation semantics: a frame is read all-or-nothing. Cancellation is only honored at the
    ///     boundary between frames (before any byte of the current frame has been consumed) — in that
    ///     case the read is abandoned cleanly and the stream remains positioned at a frame boundary, so
    ///     a subsequent <see cref="ReadFrameAsync"/> stays in sync. Once any byte of a frame's header or
    ///     payload has been consumed, the read runs to completion (cancellation is NOT observed for the
    ///     remainder), because aborting mid-frame would leave unread bytes that desync the next read on
    ///     a length-prefixed stream. To stop reading promptly, close/dispose the underlying stream; that
    ///     surfaces as an <see cref="System.IO.IOException"/> or returns <see langword="null"/> at EOF.
    /// </remarks>
    /// <param name="stream">The source stream.</param>
    /// <param name="ct">Observed only at the frame boundary, before any byte of the frame is read.</param>
    /// <param name="format">The wire format; JSON by default.</param>
    /// <param name="onFrameStarted">
    ///     Called once, when the first byte of the frame has been read: the moment a reader waiting for the
    ///     next message becomes a reader in the middle of one. A per-frame deadline is armed here, so that
    ///     a client that is quiet between messages is not mistaken for one that stalls inside a message.
    /// </param>
    public static async Task<AgentMessage?> ReadFrameAsync(
        Stream stream, CancellationToken ct = default, IWireFormat? format = null, Action? onFrameStarted = null)
    {
        var headerBuffer = new byte[4];
        // Honor cancellation only at the frame boundary, before consuming any bytes.
        if (!await ReadExactlyAsync(stream, headerBuffer, ct, allowCancellation: true, onFirstByte: onFrameStarted)
                .ConfigureAwait(false))
            return null;

        var length = BinaryPrimitives.ReadInt32BigEndian(headerBuffer);

        if (length <= 0 || length > MaxFrameSize)
            throw new InvalidOperationException($"Invalid frame length: {length}");

        var payloadBuffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            // Header already consumed: the frame is now in-flight. Read the full payload without
            // observing cancellation so we never leave a partial frame that desyncs the next read.
            if (!await ReadExactlyAsync(stream, payloadBuffer.AsMemory(0, length), ct, allowCancellation: false).ConfigureAwait(false))
                return null;

            return (format ?? DefaultFormat).Deserialize<AgentMessage>(payloadBuffer.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payloadBuffer);
        }
    }

    /// <summary>Writes a frame to a Stream asynchronously.</summary>
    public static async Task WriteFrameAsync(Stream stream, AgentMessage message, CancellationToken ct = default, IWireFormat? format = null)
    {
        var frame = Encode(message, format);
        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads exactly <paramref name="buffer"/>.Length bytes.
    /// </summary>
    /// <param name="stream">The source stream.</param>
    /// <param name="buffer">The destination buffer; its full length is read.</param>
    /// <param name="ct">Cancellation token (honored per <paramref name="allowCancellation"/>).</param>
    /// <param name="allowCancellation">
    ///     When true, the supplied token is observed only while no byte has been read yet (at a frame
    ///     boundary). Once the first byte is consumed — or when false — the read completes regardless of
    ///     the token, so a length-prefixed stream is never left mid-frame.
    /// </param>
    /// <param name="onFirstByte">Called once, after the first byte of this read has been consumed.</param>
    private static async Task<bool> ReadExactlyAsync(
        Stream stream, Memory<byte> buffer, CancellationToken ct, bool allowCancellation, Action? onFirstByte = null)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            // Observe cancellation only at the boundary: before any byte of this read is consumed.
            var readCt = allowCancellation && offset == 0 ? ct : CancellationToken.None;
            var read = await stream.ReadAsync(buffer[offset..], readCt).ConfigureAwait(false);
            if (read == 0) return false;
            if (offset == 0)
                onFirstByte?.Invoke();
            offset += read;
        }

        return true;
    }
}
