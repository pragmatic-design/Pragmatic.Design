// Pragmatic Agent — coordination daemon + CLI
// Dual mode: "pragmatic-agent start" (daemon) or "pragmatic <command>" (CLI)

using Pragmatic.Agent.Cli;
using Pragmatic.Agent.Configuration;
using Pragmatic.Agent.Gossip;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Platform;
using Pragmatic.Agent.Socket;

// CLI mode: if args start with a known command, act as CLI client
if (args.Length > 0 && args[0] is not "start" and not "--daemon")
{
    return await CliHandler.ExecuteAsync(args).ConfigureAwait(false);
}

// Parse daemon args
string? instanceName = null;
string? socketOverride = null;
string? dataDirOverride = null;
string? platformOverride = null;
var clusterConfig = new ClusterConfig();

for (var i = 0; i < args.Length; i++)
{
    var hasNext = i + 1 < args.Length;
    switch (args[i])
    {
        case "--instance" when hasNext: instanceName = args[++i]; break;
        case "--socket" when hasNext: socketOverride = args[++i]; break;
        case "--data-dir" when hasNext: dataDirOverride = args[++i]; break;
        case "--port" when hasNext:
            if (!int.TryParse(args[++i], out var gossipPort))
            {
                Console.Error.WriteLine($"Invalid value for --port: '{args[i]}'. Expected an integer.");
                return 1;
            }
            clusterConfig.GossipPort = gossipPort;
            break;
        case "--peers" when hasNext: clusterConfig.Peers = args[++i].Split(',').ToList(); clusterConfig.Discovery = "static"; break;
        case "--discovery" when hasNext: clusterConfig.Discovery = args[++i]; break;
        case "--bind" when hasNext: clusterConfig.BindAddress = args[++i]; break;
        case "--gossip-key" when hasNext: clusterConfig.SharedKey = args[++i]; break;
        case "--platform" when hasNext: platformOverride = args[++i]; break;
    }
}

// Resolve all paths (instance-aware, environment-aware)
var paths = new AgentPaths(instanceName, socketOverride, dataDirOverride);
paths.EnsureDirectories();

// Persistent agent ID (survives restarts, consistent in gossip cluster)
clusterConfig.AgentId = paths.GetOrCreateAgentId();

Console.WriteLine($"Pragmatic Agent v0.1.0 [{clusterConfig.AgentId}]");
Console.WriteLine(paths.ToString());
Console.WriteLine($"Gossip: :{clusterConfig.GossipPort} ({clusterConfig.Discovery})");

// Secret encryption (auto-detects key from env or file). Built BEFORE the KV store because the
// store owns at-rest encryption for secret/* keys: this guarantees secrets are ciphertext on disk
// AND on the gossip wire (gossip replicates the stored value verbatim).
var secretEncryptor = KvSecretProtector.CreateFromEnvironment(paths.DataDirectory);

// Initialize KV store with file persistence
var kvStore = new KvStore(secretEncryptor);
var persistence = new KvFilePersistence(kvStore, paths.KvFilePath);
persistence.Load();
persistence.StartPeriodicFlush();

Console.WriteLine($"KV loaded: {kvStore.GetAll().Count} entries (secrets: {(secretEncryptor is NoEncryption ? "plaintext" : "AES-256-GCM")})");

// Start gossip cluster
using var cluster = new ClusterManager(clusterConfig, kvStore);
await cluster.StartAsync().ConfigureAwait(false);

// Start socket server (KvStore owns secret encryption now, so the handler needs only the store)
var handler = new AgentMessageHandler(kvStore, clusterConfig.AgentId);
using var server = new AgentSocketServer(paths.SocketPath, handler);
server.Start();

// Push KV changes to connected app clients so client-side Watch/StreamEvents are event-driven
// instead of poll-only. Without this, KvChanged frames are never emitted by the daemon.
using var kvBroadcaster = new KvChangeBroadcaster(kvStore, server);

// Deliver host commands (commands/{instanceId}/{id}) to the target instance when it is connected here, and
// flush any queued commands when an instance registers. This makes AgentControlPlane.SendCommandAsync a
// real delivery path instead of a dead KV write.
using var commandPump = new CommandDispatchPump(kvStore, server);
handler.InstanceRegistered = commandPump.FlushPendingFor;

// Detect the deployment platform and enforce per-app maintenance on it: when a local app enters
// Maintenance (its state/app: descriptor), the platform adapter activates the native mechanism (e.g.
// an nginx/IIS maintenance config); leaving Maintenance deactivates it. Auto-detected from the
// environment (K8s/ACA/ECS/Docker/IIS/nginx); override with --platform.
var platformAdapter = PlatformDetector.Detect(platformOverride);
Console.WriteLine($"Platform: {platformAdapter.PlatformName}");
using var maintenanceEnforcer = new PlatformMaintenanceEnforcer(kvStore, server, platformAdapter);

Console.WriteLine("Agent ready. Listening for connections...");

// Wait for shutdown
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await Task.Delay(-1, cts.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Shutting down...");
}

persistence.Dispose();
Console.WriteLine("Agent stopped.");
return 0;
