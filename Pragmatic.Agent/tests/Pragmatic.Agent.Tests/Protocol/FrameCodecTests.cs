using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Protocol;
using Xunit;

namespace Pragmatic.Agent.Tests.Protocol;

public class FrameCodecTests
{
    [Fact]
    public void Encode_Decode_Roundtrip()
    {
        var original = new AgentMessage
        {
            Type = MessageType.KvGet,
            Id = "42",
            Payload = JsonSerializer.SerializeToElement(new { key = "test" })
        };

        var encoded = FrameCodec.Encode(original);
        var (decoded, bytesConsumed) = FrameCodec.TryDecode(encoded);

        decoded.Should().NotBeNull();
        decoded!.Type.Should().Be(MessageType.KvGet);
        decoded.Id.Should().Be("42");
        bytesConsumed.Should().Be(encoded.Length);
    }

    [Fact]
    public void TryDecode_IncompleteHeader_ReturnsNull()
    {
        var buffer = new byte[] { 0, 0 }; // Only 2 bytes, need 4
        var (message, consumed) = FrameCodec.TryDecode(buffer);

        message.Should().BeNull();
        consumed.Should().Be(0);
    }

    [Fact]
    public void TryDecode_IncompletePayload_ReturnsNull()
    {
        var original = new AgentMessage { Type = MessageType.Heartbeat, Id = "1" };
        var encoded = FrameCodec.Encode(original);

        // Truncate payload
        var truncated = encoded.AsSpan(0, encoded.Length - 5);
        var (message, consumed) = FrameCodec.TryDecode(truncated);

        message.Should().BeNull();
        consumed.Should().Be(0);
    }

    [Fact]
    public async Task ReadFrame_WriteFrame_StreamRoundtrip()
    {
        var original = new AgentMessage
        {
            Type = MessageType.Response,
            Id = "99",
            Success = true
        };

        using var stream = new MemoryStream();
        await FrameCodec.WriteFrameAsync(stream, original);
        stream.Position = 0;

        var decoded = await FrameCodec.ReadFrameAsync(stream);

        decoded.Should().NotBeNull();
        decoded!.Type.Should().Be(MessageType.Response);
        decoded.Id.Should().Be("99");
        decoded.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ReadFrame_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();
        var decoded = await FrameCodec.ReadFrameAsync(stream);

        decoded.Should().BeNull();
    }

    [Fact]
    public void Encode_MultipleMessages_DecodableSequentially()
    {
        var msg1 = new AgentMessage { Type = MessageType.Register, Id = "1" };
        var msg2 = new AgentMessage { Type = MessageType.Heartbeat, Id = "2" };

        var encoded1 = FrameCodec.Encode(msg1);
        var encoded2 = FrameCodec.Encode(msg2);

        var combined = new byte[encoded1.Length + encoded2.Length];
        encoded1.CopyTo(combined, 0);
        encoded2.CopyTo(combined, encoded1.Length);

        var (decoded1, consumed1) = FrameCodec.TryDecode(combined);
        decoded1.Should().NotBeNull();
        decoded1!.Id.Should().Be("1");

        var (decoded2, consumed2) = FrameCodec.TryDecode(combined.AsSpan(consumed1));
        decoded2.Should().NotBeNull();
        decoded2!.Id.Should().Be("2");
    }
}
