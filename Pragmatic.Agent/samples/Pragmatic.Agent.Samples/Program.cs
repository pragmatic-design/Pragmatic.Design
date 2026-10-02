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
//   4. AgentOptionsSample — client configuration defaults
//
// The daemon-only subsystems (KvStore CAS/watch, KvSecretProtector at-rest
// encryption, AgentSocketServer, gossip membership) are `internal` to the
// Pragmatic.Agent executable and are described in the README, not run here.
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("Pragmatic.Agent Samples");
Console.WriteLine("=======================");
Console.WriteLine("(In-process protocol + client config only — no daemon, no socket.)");
Console.WriteLine();

WireFormatSample.Run();
await FrameCodecSample.RunAsync();
AgentMessageSample.Run();
AgentOptionsSample.Run();

Console.WriteLine("All samples completed.");
