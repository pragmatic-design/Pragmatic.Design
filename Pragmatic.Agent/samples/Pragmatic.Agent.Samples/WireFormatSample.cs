using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Protocol.Serialization;

namespace Pragmatic.Agent.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 1 — IWireFormat serialize/deserialize roundtrip.
//
// IWireFormat is the pluggable serialization the Agent uses on the socket and for
// gossip. JsonWireFormat is the live default (used by FrameCodec). A binary
// (MessagePack) format was prototyped but removed: the message model is
// JSON-native (AgentMessage.Payload is a JsonElement), so a real binary format
// needs a format-neutral envelope + self-describing frames — see
// docs/future/agent-transport-security-and-binary-wireformat.md. The extension
// point (IWireFormat) stays, so a future format plugs into the same interface.
// ─────────────────────────────────────────────────────────────────────────────

internal static class WireFormatSample
{
    public static void Run()
    {
        Console.WriteLine("== Sample 1: IWireFormat roundtrip ==");
        Console.WriteLine();

        var payload = new KvSetPayload
        {
            Key = "config/booking-service/retry-max-attempts",
            Value = "3",
            ExpectedVersion = 7,
        };

        // JSON — the default format, used by FrameCodec and the socket today.
        var json = JsonWireFormat.Instance;
        var bytes = json.Serialize(payload);
        var decoded = json.Deserialize<KvSetPayload>(bytes);

        var ok = decoded is not null
                 && decoded.Key == payload.Key
                 && decoded.Value == payload.Value
                 && decoded.ExpectedVersion == payload.ExpectedVersion;

        Console.WriteLine($"  {json.FormatName,-8} : {bytes.Length,3} bytes -> roundtrip {(ok ? "OK" : "FAILED")}");
        Console.WriteLine($"             key='{decoded?.Key}', value='{decoded?.Value}', expectedVersion={decoded?.ExpectedVersion}");
        Console.WriteLine();
    }
}
