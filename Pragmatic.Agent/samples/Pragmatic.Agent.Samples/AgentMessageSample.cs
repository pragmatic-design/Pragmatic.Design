using System.Text.Json;
using Pragmatic.Agent.Protocol;
using Pragmatic.Agent.Protocol.Payloads;

namespace Pragmatic.Agent.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 3 — AgentMessage envelope + MessageType + typed payload roundtrip.
//
// Every Agent request/response is an AgentMessage: a MessageType discriminator,
// a correlation Id, and a JsonElement Payload deserialized lazily by the handler.
// This sample mirrors the daemon's real request/response shape (see
// AgentMessageHandler): build a KvSet request, extract its typed payload exactly
// as the handler does (message.Payload.Value.Deserialize<T>()), then build the
// matching Response carrying a KvSetResultPayload via
// JsonSerializer.SerializeToElement — all in-process, no socket.
// ─────────────────────────────────────────────────────────────────────────────

internal static class AgentMessageSample
{
    public static void Run()
    {
        Console.WriteLine("== Sample 3: AgentMessage + MessageType + payload ==");
        Console.WriteLine();

        // --- App -> Agent: a KvSet request -----------------------------------
        var request = new AgentMessage
        {
            Type = MessageType.KvSet,
            Id = "corr-42",
            Payload = JsonSerializer.SerializeToElement(new KvSetPayload
            {
                Key = "tenants/acme-eu",
                Value = "{\"region\":\"eu-west-1\"}",
                ExpectedVersion = null, // unconditional write (no CAS)
            }),
        };

        Console.WriteLine($"  Request : Type={request.Type} (#{(int)request.Type}), Id={request.Id}");

        // Extract the typed payload the way the handler does.
        var set = request.Payload!.Value.Deserialize<KvSetPayload>()!;
        Console.WriteLine($"            payload -> key='{set.Key}', value='{set.Value}', CAS={set.ExpectedVersion?.ToString() ?? "none"}");
        Console.WriteLine();

        // --- Agent -> App: the matching Response -----------------------------
        var response = new AgentMessage
        {
            Type = MessageType.Response,
            Id = request.Id, // correlation id echoed back
            Success = true,
            Payload = JsonSerializer.SerializeToElement(new KvSetResultPayload
            {
                Version = 8,
                CasConflict = false,
            }),
        };

        var result = response.Payload!.Value.Deserialize<KvSetResultPayload>()!;
        Console.WriteLine($"  Response: Type={response.Type} (#{(int)response.Type}), Id={response.Id}, Success={response.Success}");
        Console.WriteLine($"            payload -> version={result.Version}, casConflict={result.CasConflict}");
        Console.WriteLine();

        // --- An error response (no payload, carries an Error string) ---------
        var error = new AgentMessage
        {
            Type = MessageType.Response,
            Id = request.Id,
            Success = false,
            Error = "No encryption key configured.",
        };
        Console.WriteLine($"  Error   : Success={error.Success}, Error='{error.Error}'");
        Console.WriteLine();
    }
}
