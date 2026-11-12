using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     A TCP relay between a MongoDB client and the server that runs a step of the test's before it passes on the first command whose bytes
///     contain a given text: the test puts its own write between two commands of the code under test, on every run, instead of hoping a
///     concurrent writer lands there.
/// </summary>
/// <remarks>
///     Messages are recognised by the wire protocol's framing alone: each starts with its length, a little-endian int32 that counts itself.
///     The server's replies are copied back as they come. Every connection the client's pool opens goes through the relay.
/// </remarks>
public sealed class MongoCommandRelay : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _host;
    private readonly int _port;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _pumps = [];
    private readonly Lock _pumpsLock = new();
    private readonly List<TcpClient> _sockets = [];

    private Task _accepting = Task.CompletedTask;
    private byte[]? _pattern;
    private int _remaining;
    private Func<Task>? _before;

    private MongoCommandRelay(string host, int port)
    {
        _host = host;
        _port = port;
    }

    /// <summary>The port the relay listens on, on 127.0.0.1.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Starts a relay in front of the server at <paramref name="host" />:<paramref name="port" />.</summary>
    public static MongoCommandRelay Start(string host, int port)
    {
        var relay = new MongoCommandRelay(host, port);
        relay._listener.Start();
        relay._accepting = relay.AcceptAsync();
        return relay;
    }

    /// <summary>
    ///     The <paramref name="occurrence" />-th command from now whose bytes contain <paramref name="text" /> reaches the server only after
    ///     <paramref name="before" /> has completed; the others pass at once.
    /// </summary>
    public void Before(string text, int occurrence, Func<Task> before)
    {
        Volatile.Write(ref _before, before);
        Volatile.Write(ref _remaining, occurrence);
        Volatile.Write(ref _pattern, Encoding.UTF8.GetBytes(text));
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false);
                var upstream = new TcpClient();
                await upstream.ConnectAsync(_host, _port, _stopping.Token).ConfigureAwait(false);
                lock (_pumpsLock)
                {
                    _sockets.Add(client);
                    _sockets.Add(upstream);
                    _pumps.Add(PumpToServerAsync(client.GetStream(), upstream.GetStream()));
                    _pumps.Add(PumpToClientAsync(client.GetStream(), upstream.GetStream()));
                }
            }
        }
        catch (Exception ex) when (IsTeardown(ex))
        {
            // Disposed: no more connections to accept.
        }
    }

    private async Task PumpToServerAsync(NetworkStream fromClient, NetworkStream toServer)
    {
        var ct = _stopping.Token;
        var length = new byte[4];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await fromClient.ReadExactlyAsync(length, ct).ConfigureAwait(false);
                var message = new byte[BinaryPrimitives.ReadInt32LittleEndian(length)];
                length.CopyTo(message, 0);
                await fromClient.ReadExactlyAsync(message.AsMemory(4), ct).ConfigureAwait(false);

                var pattern = Volatile.Read(ref _pattern);
                if (pattern is not null && message.AsSpan().IndexOf(pattern) >= 0 && Interlocked.Decrement(ref _remaining) == 0)
                {
                    Volatile.Write(ref _pattern, null);
                    await Volatile.Read(ref _before)!().ConfigureAwait(false);
                }

                await toServer.WriteAsync(message, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsTeardown(ex))
        {
            // One side closed the connection: the relay has nothing left to carry.
        }
    }

    private async Task PumpToClientAsync(NetworkStream toClient, NetworkStream fromServer)
    {
        try
        {
            await fromServer.CopyToAsync(toClient, _stopping.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTeardown(ex))
        {
            // One side closed the connection: the relay has nothing left to carry.
        }
    }

    private static bool IsTeardown(Exception ex)
        => ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException or EndOfStreamException;

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        Task[] pumps;
        lock (_pumpsLock)
        {
            foreach (var socket in _sockets)
                socket.Dispose();
            pumps = [.. _pumps];
        }

        await _accepting.ConfigureAwait(false);
        await Task.WhenAll(pumps).ConfigureAwait(false);
        _listener.Dispose();
        _stopping.Dispose();
    }
}
