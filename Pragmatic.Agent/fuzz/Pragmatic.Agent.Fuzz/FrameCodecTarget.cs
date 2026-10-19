using System.Buffers.Binary;
using System.Text.Json;
using Pragmatic.Agent.Protocol;

namespace Pragmatic.Agent.Fuzz;

/// <summary>
///     The agent's frame codec on bytes a peer controls (#307), through both of its readers.
/// </summary>
/// <remarks>
///     The codec's declared outcomes are a message with the bytes it consumed, "not enough bytes yet", an
///     <see cref="InvalidOperationException" /> for a length out of range and a <see cref="JsonException" /> for a
///     payload that is not a message. Those return; anything else escapes and is a crash, and so is an outcome that
///     contradicts the bytes (<see cref="FuzzFinding" />).
/// </remarks>
public static class FrameCodecTarget
{
    private const int MaxFrameSize = 16 * 1024 * 1024;

    public static void Run(ReadOnlySpan<byte> data)
    {
        Decode(data);
        Read(data.ToArray());
    }

    private static void Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            var (message, consumed) = FrameCodec.TryDecode(data);
            if (message is null && consumed != 0)
                throw new FuzzFinding($"No message, but {consumed} bytes consumed.");
            if (message is not null && consumed != 4 + Length(data))
                throw new FuzzFinding($"A message from {consumed} bytes of a frame of {4 + Length(data)}.");
        }
        catch (InvalidOperationException) when (data.Length >= 4 && OutOfRange(Length(data)))
        {
        }
        catch (JsonException) when (data.Length >= 4 + Length(data))
        {
        }
    }

    private static void Read(byte[] data)
    {
        using var stream = new MemoryStream(data);
        try
        {
            // The stream is in memory: the read completes synchronously, and libFuzzer calls the harness on one thread.
            var message = FrameCodec.ReadFrameAsync(stream).GetAwaiter().GetResult();
            if (message is null && data.Length >= 4 && !OutOfRange(Length(data)) && data.Length >= 4 + Length(data))
                throw new FuzzFinding("No message from a whole frame.");
        }
        catch (InvalidOperationException) when (data.Length >= 4 && OutOfRange(Length(data)))
        {
        }
        catch (JsonException) when (data.Length >= 4 + Length(data))
        {
        }
    }

    private static int Length(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadInt32BigEndian(data[..4]);

    private static bool OutOfRange(int length) => length <= 0 || length > MaxFrameSize;
}
