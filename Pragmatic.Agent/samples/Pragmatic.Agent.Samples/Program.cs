using Pragmatic.Agent.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Pragmatic.Agent — runnable samples.
//
// Pragmatic.Agent is a local coordination daemon (KV store, SWIM gossip, IPC
// over a Unix socket / Windows named pipe). A console sample MUST NOT spin up
// the daemon or open a socket, so these samples cover only the pieces that run
// purely in-process from the PUBLIC libraries:
//   1. WireFormatSample   — JSON + MessagePack serialize/deserialize roundtrip
//   2. FrameCodecSample   — length-prefixed framing roundtrip over a MemoryStream
//   3. AgentMessageSample — AgentMessage + MessageType + typed payload roundtrip
//
// The client options (AgentOptions) are not shown here: referencing Pragmatic.Agent.Client from
// this small executable made Bitdefender quarantine it (see the README).
//
// The daemon-only subsystems (KvStore CAS/watch, KvSecretProtector at-rest
// encryption, AgentSocketServer, gossip membership) are `internal` to the
// Pragmatic.Agent executable and are described in the README, not run here.
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("Pragmatic.Agent Samples");
Console.WriteLine("=======================");
Console.WriteLine("(In-process protocol only — no daemon, no socket.)");
Console.WriteLine();

WireFormatSample.Run();
await FrameCodecSample.RunAsync();
AgentMessageSample.Run();

Console.WriteLine("All samples completed.");
