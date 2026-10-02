using System.Text.Json;
using Pragmatic.Agent.Protocol;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Protocol.Serialization;

namespace Pragmatic.Agent.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 2 — FrameCodec length-prefixed framing roundtrip.
//
// The Agent socket protocol is "[4-byte big-endian length][serialized payload]".
// FrameCodec encodes/decodes those frames over any Stream. Here we exercise it
// fully IN-PROCESS over a MemoryStream — no socket, no daemon — by:
//   • Encode()/TryDecode() on a byte buffer (the sync path), and
//   • WriteFrameAsync()/ReadFrameAsync() over a MemoryStream (the async path),
// writing two messages back-to-back to prove the length prefix keeps the stream
// in sync across multiple frames.
// ─────────────────────────────────────────────────────────────────────────────

internal static class FrameCodecSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("== Sample 2: FrameCodec length-prefixed framing ==");
        Console.WriteLine();

        var getMessage = new AgentMessage
        {
            Type = MessageType.KvGet,
            Id = "req-1",
            Payload = ToElement(new KvGetPayload { Key = "flags/new-checkout-flow" }),
        };

        // --- Sync path: Encode -> TryDecode --------------------------------
        var frame = FrameCodec.Encode(getMessage);
        var (decoded, consumed) = FrameCodec.TryDecode(frame);
        Console.WriteLine("  Encode/TryDecode (sync):");
        Console.WriteLine($"      frame = {frame.Length} bytes (4-byte length prefix + {frame.Length - 4}-byte body)");
        Console.WriteLine($"      decoded Type={decoded?.Type}, Id={decoded?.Id}, bytesConsumed={consumed}");
        Console.WriteLine();

        // --- Async path over an in-memory stream: two frames back-to-back --
        var setMessage = new AgentMessage
        {
            Type = MessageType.KvSet,
            Id = "req-2",
            Payload = ToElement(new KvSetPayload { Key = "flags/new-checkout-flow", Value = "enabled:true" }),
        };

        using var stream = new MemoryStream();
        await FrameCodec.WriteFrameAsync(stream, getMessage);
        await FrameCodec.WriteFrameAsync(stream, setMessage);
        stream.Position = 0;

        Console.WriteLine("  WriteFrameAsync/ReadFrameAsync over MemoryStream:");
        var first = await FrameCodec.ReadFrameAsync(stream);
        var second = await FrameCodec.ReadFrameAsync(stream);
        var eof = await FrameCodec.ReadFrameAsync(stream);

        Console.WriteLine($"      frame 1 : Type={first?.Type}, Id={first?.Id}");
        Console.WriteLine($"      frame 2 : Type={second?.Type}, Id={second?.Id}");
        Console.WriteLine($"      frame 3 : {(eof is null ? "null (clean EOF)" : "unexpected")}");
        Console.WriteLine();
    }

    private static JsonElement ToElement<T>(T payload)
    {
        var bytes = JsonWireFormat.Instance.Serialize(payload);
        return JsonSerializer.Deserialize<JsonElement>(bytes);
    }
}
