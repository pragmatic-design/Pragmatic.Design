using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Agent.Protocol;
using Xunit;

namespace Pragmatic.Agent.Tests.Protocol;

/// <summary>
///     The frame codec reads bytes off a connection a peer controls (#158, #307). For any input it has three
///     outcomes and no others: a message with the bytes it consumed, "not enough bytes yet", or its own refusal: an
///     <see cref="InvalidOperationException" /> for a length out of range, a <see cref="JsonException" /> for a
///     payload that is not a message.
/// </summary>
public class FrameCodecPropertyTests
{
    private const int MaxFrameSize = 16 * 1024 * 1024;

    // Payloads a peer can send in a well-formed frame: JSON of every shape, a message, and text that is not JSON.
    private static readonly string[] Fragments =
    [
        "null", "{}", "[]", "0", "\"\"", "true", "{\"type\":0}", "{\"type\":\"KvGet\",\"id\":\"1\"}",
        "{\"type\":99999}", "{\"payload\":null}", "{\"success\":\"yes\"}", "{", "}", "not json", "﻿{}",
    ];

    private static readonly Arbitrary<byte[]> Frames = Gen.OneOf(
            Gen.Elements(Fragments),
            ArbMap.Default.GeneratorFor<string>().Where(s => s is not null),
            Gen.Elements(Fragments).Two().Select(t => t.Item1 + t.Item2))
        .Select(Frame)
        .ToArbitrary();

    [Fact]
    public void Any_bytes_decode_to_a_declared_outcome()
        => Prop.ForAll(ArbMap.Default.ArbFor<byte[]>(), TryDecodeIsDeclared).QuickCheckThrowOnFailure();

    [Fact]
    public void Any_payload_in_a_well_formed_frame_decodes_to_a_declared_outcome()
        => Prop.ForAll(Frames, TryDecodeIsDeclared).QuickCheckThrowOnFailure();

    [Fact]
    public void Any_bytes_read_from_a_stream_give_a_declared_outcome()
        => Prop.ForAll(ArbMap.Default.ArbFor<byte[]>(), ReadFrameIsDeclared).QuickCheckThrowOnFailure();

    [Fact]
    public void Any_payload_read_from_a_stream_gives_a_declared_outcome()
        => Prop.ForAll(Frames, ReadFrameIsDeclared).QuickCheckThrowOnFailure();

    [Fact]
    public void Any_message_decodes_to_itself()
    {
        var messages =
            from type in Gen.Elements(Enum.GetValues<MessageType>())
            from id in ArbMap.Default.GeneratorFor<string?>()
            from error in ArbMap.Default.GeneratorFor<string?>()
            from success in ArbMap.Default.GeneratorFor<bool?>()
            select new AgentMessage { Type = type, Id = id, Error = error, Success = success };

        Prop.ForAll(messages.ToArbitrary(), message =>
        {
            var (decoded, consumed) = FrameCodec.TryDecode(FrameCodec.Encode(message));
            return decoded is not null
                   && consumed == FrameCodec.Encode(message).Length
                   && decoded.Type == message.Type && decoded.Id == message.Id
                   && decoded.Error == message.Error && decoded.Success == message.Success;
        }).QuickCheckThrowOnFailure();
    }

    // A message with the bytes of its frame, or nothing and nothing consumed; the length refusal only for a length
    // out of range, and a JSON refusal only for a frame that is all there.
    private static bool TryDecodeIsDeclared(byte[] buffer)
    {
        try
        {
            var (message, consumed) = FrameCodec.TryDecode(buffer);
            if (message is null)
                return consumed == 0 && (buffer.Length < 4 || buffer.Length < 4 + Length(buffer));
            return consumed == 4 + Length(buffer) && consumed <= buffer.Length;
        }
        catch (InvalidOperationException)
        {
            return buffer.Length >= 4 && OutOfRange(Length(buffer));
        }
        catch (JsonException)
        {
            return buffer.Length >= 4 && !OutOfRange(Length(buffer)) && buffer.Length >= 4 + Length(buffer);
        }
    }

    // A message, or nothing because the stream ended before the frame did.
    private static bool ReadFrameIsDeclared(byte[] buffer)
    {
        using var stream = new MemoryStream(buffer);
        try
        {
            var message = FrameCodec.ReadFrameAsync(stream).GetAwaiter().GetResult();
            return message is not null || buffer.Length < 4 || buffer.Length < 4 + Length(buffer);
        }
        catch (InvalidOperationException)
        {
            return buffer.Length >= 4 && OutOfRange(Length(buffer));
        }
        catch (JsonException)
        {
            return buffer.Length >= 4 && !OutOfRange(Length(buffer)) && buffer.Length >= 4 + Length(buffer);
        }
    }

    private static int Length(byte[] buffer) => BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4));

    private static bool OutOfRange(int length) => length <= 0 || length > MaxFrameSize;

    private static byte[] Frame(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var frame = new byte[4 + bytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, bytes.Length);
        bytes.CopyTo(frame, 4);
        return frame;
    }
}
