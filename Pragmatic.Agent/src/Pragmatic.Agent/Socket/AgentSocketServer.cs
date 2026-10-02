using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Pragmatic.Agent.Protocol;

namespace Pragmatic.Agent.Socket;

/// <summary>
///     Listens on a Unix domain socket (Linux/macOS) or named pipe (Windows)
///     and accepts client connections. Each connection is handled by <see cref="ClientConnection"/>.
/// </summary>
internal sealed class AgentSocketServer(string socketPath, IMessageHandler handler) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly List<ClientConnection> _connections = [];
    private readonly Lock _lock = new();
    private Task? _listenTask;

    /// <summary>Opens the endpoint, then accepts clients in the background.</summary>
    /// <remarks>
    ///     The endpoint is opened here, synchronously, so a server that cannot listen fails its caller.
    ///     Opened inside the accept loop, it would fail out of sight: on Unix the exception goes into a
    ///     task nobody observes and Start() returns as if listening; on Windows a pipe that cannot be
    ///     created is retried with no await in between, and Start() never returns.
    /// </remarks>
    public void Start()
    {
        if (OperatingSystem.IsWindows())
            _listenTask = ListenNamedPipeAsync(CreateRestrictedNamedPipeServer(Path.GetFileName(socketPath)), _cts.Token);
        else
            _listenTask = AcceptUnixSocketAsync(OpenUnixSocket(), _cts.Token);
    }

    /// <summary>Sends a message to all connected clients.</summary>
    public async Task BroadcastAsync(AgentMessage message, CancellationToken ct = default)
    {
        ClientConnection[] snapshot;
        lock (_lock) { snapshot = _connections.ToArray(); }

        foreach (var conn in snapshot)
        {
            try
            {
                await conn.SendAsync(message, ct).ConfigureAwait(false);
            }
            catch
            {
                // Client disconnected — will be cleaned up
            }
        }
    }

    /// <summary>Whether an app with the given id is currently connected to THIS daemon (i.e. hosted here).</summary>
    public bool IsAppConnected(string appId)
    {
        lock (_lock) { return _connections.Exists(c => c.AppId == appId); }
    }

    /// <summary>
    ///     Sends a message to one instance, the connection that registered with <paramref name="instanceId" />;
    ///     a no-op when it is not connected here.
    /// </summary>
    public async Task SendToInstanceAsync(string instanceId, AgentMessage message, CancellationToken ct = default)
    {
        ClientConnection? target;
        lock (_lock) { target = _connections.FirstOrDefault(c => c.InstanceId == instanceId); }

        if (target is not null)
            await target.SendAsync(message, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _cts.Cancel();

        lock (_lock)
        {
            foreach (var conn in _connections)
                conn.Dispose();
            _connections.Clear();
        }

        // Cleanup Unix socket file. Use symlink-safe delete: refuse to follow a link put there by
        // another user (which would otherwise delete an arbitrary file).
        if (!OperatingSystem.IsWindows())
            TryRemoveUnixSocketFile(socketPath);

        _cts.Dispose();
    }

    [UnsupportedOSPlatform("windows")]
    private System.Net.Sockets.Socket OpenUnixSocket()
    {
        // Ensure directory exists with restrictive permissions (owner-only access).
        // A world-writable parent directory would allow a symlink-hijack of the socket path.
        // A bare name (the form a Windows pipe name takes) has no directory: GetDirectoryName returns ""
        // for it, not null, and CreateDirectory("") throws.
        var dir = Path.GetDirectoryName(socketPath);
        if (!string.IsNullOrEmpty(dir))
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            try
            {
                File.SetUnixFileMode(dir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch { /* best effort — user may have intentionally shared the dir */ }
        }

        // Remove stale socket file (symlink-safe).
        TryRemoveUnixSocketFile(socketPath);

        var endpoint = new UnixDomainSocketEndPoint(socketPath);
        var listener = new System.Net.Sockets.Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(endpoint);
            listener.Listen(32);
        }
        catch
        {
            listener.Dispose();
            throw;
        }

        // Security model: the socket file is owner-only (0600) and its directory is 0700.
        // The trust boundary is therefore the agent's own uid — the standard model for a
        // per-user daemon socket (cf. the Docker / systemd user sockets). Group-readable
        // permissions would let any user in the agent's group read KV contents (config,
        // tenant map, etc.), which is why they are explicitly denied. Additional peer
        // credential pinning (SO_PEERCRED) would be redundant with the 0600 file mode and
        // is intentionally not used; it would only matter if the file mode were loosened.
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* Best effort */ }
        }

        return listener;
    }

    [UnsupportedOSPlatform("windows")]
    private async Task AcceptUnixSocketAsync(System.Net.Sockets.Socket listener, CancellationToken ct)
    {
        using var owned = listener;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await listener.AcceptAsync(ct).ConfigureAwait(false);
                var stream = new NetworkStream(clientSocket, ownsSocket: true);
                AcceptConnection(stream);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AgentLogger.Error("Socket", $"Accept error: {ex.Message}");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task ListenNamedPipeAsync(NamedPipeServerStream first, CancellationToken ct)
    {
        var pipeName = Path.GetFileName(socketPath);
        // The first instance is created by Start(), so a name that cannot be served fails there.
        NamedPipeServerStream? pipe = first;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Build an ACL that grants access only to the current user (pipe owner).
                // Default NamedPipeServerStream grants authenticated users — which means any other
                // logged-in account on the host can read/write KV state via the pipe.
                pipe ??= CreateRestrictedNamedPipeServer(pipeName);

                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                AcceptConnection(pipe);
                pipe = null;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // A fresh instance on the next turn, as before; this one is not reused after a failure.
                pipe?.Dispose();
                pipe = null;
                AgentLogger.Error("Socket", $"Pipe accept error: {ex.Message}");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateRestrictedNamedPipeServer(string pipeName)
    {
        var security = new PipeSecurity();
        var ownerSid = WindowsIdentity.GetCurrent().Owner;
        if (ownerSid is not null)
        {
            // CreateNewInstance is REQUIRED: the accept loop opens a fresh server instance per client,
            // and without granting the owner this right every instance after the first fails with
            // "Access to the path is denied" — so the daemon could serve only a single app at a time.
            security.AddAccessRule(new PipeAccessRule(
                ownerSid,
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));
        }
        // SYSTEM may need access for diagnostics/services in some deployments.
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(
            systemSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private static void TryRemoveUnixSocketFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            // Refuse to follow symlinks — would delete the link target, which could be an unrelated
            // file pointed to by an attacker who reached the socket directory before us.
            if (info.LinkTarget is not null) return;
            File.Delete(path);
        }
        catch { /* best effort */ }
    }

    private void AcceptConnection(Stream stream)
    {
        var conn = new ClientConnection(stream, handler, OnDisconnected);
        lock (_lock) { _connections.Add(conn); }
        conn.Start();
    }

    private void OnDisconnected(ClientConnection conn)
    {
        lock (_lock) { _connections.Remove(conn); }
        // Let the handler retire the app's roster entry (state/app:) before we drop the connection.
        try { handler.OnClientDisconnected(conn); }
        catch (Exception ex) { AgentLogger.Error("Socket", $"OnClientDisconnected failed: {ex.Message}"); }
        conn.Dispose();
    }
}
