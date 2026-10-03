using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     A TCP relay between one AMQP client and the broker that can hold back one client frame, so a test
///     orders two channels' operations on the broker instead of hoping a race lands its way.
/// </summary>
/// <remarks>
///     <para>
///         A race between two channels of one connection is a round trip wide; run concurrently, the
///         pairs land in it a few times in ten, and fewer the more of them there are. Held here, the
///         first frame that names a given exchange — its declaration — reaches the broker only after
///         everything sent behind it, every time.
///     </para>
///     <para>
///         Frames are recognised by the AMQP 0-9-1 framing alone: a type byte, a channel, a size, the
///         payload and the 0xCE end marker, after the 8-byte protocol header. Holding a whole frame and
///         sending later frames of other channels first is a legal reordering: the broker keeps order
///         per channel only.
///     </para>
/// </remarks>
public sealed class AmqpFrameRelay : IAsyncDisposable
{
    private const int ProtocolHeaderLength = 8;
    private const int FrameHeaderLength = 7;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Uri _broker;
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _toBrokerLock = new(1, 1);
    private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Task> _pumps = [];

    private Task _accepting = Task.CompletedTask;
    private TcpClient? _client;
    private TcpClient? _upstream;
    private byte[]? _holdPattern;
    private byte[]? _heldFrame;

    private AmqpFrameRelay(Uri broker) => _broker = broker;

    /// <summary>Completes when the frame asked for by <see cref="HoldFirstFrameMentioning" /> has been held.</summary>
    public Task Held => _held.Task;

    /// <summary>Starts a relay in front of the broker at <paramref name="brokerConnectionString" />.</summary>
    public static AmqpFrameRelay Start(string brokerConnectionString)
    {
        var relay = new AmqpFrameRelay(new Uri(brokerConnectionString));
        relay._listener.Start();
        relay._accepting = relay.AcceptOneAsync();
        return relay;
    }

    /// <summary>The broker's connection string with the relay in its place.</summary>
    public string ConnectionString
        => new UriBuilder(_broker) { Host = "127.0.0.1", Port = ((IPEndPoint)_listener.LocalEndpoint).Port }.Uri.ToString();

    /// <summary>The next client frame whose bytes contain <paramref name="text" /> is held until <see cref="ReleaseAsync" />.</summary>
    public void HoldFirstFrameMentioning(string text) => Volatile.Write(ref _holdPattern, Encoding.UTF8.GetBytes(text));

    /// <summary>Sends the held frame on to the broker.</summary>
    public async Task ReleaseAsync()
    {
        var frame = Interlocked.Exchange(ref _heldFrame, null)
            ?? throw new InvalidOperationException("No frame is held.");
        await WriteToBrokerAsync(frame).ConfigureAwait(false);
    }

    private async Task AcceptOneAsync()
    {
        try
        {
            _client = await _listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false);
            _upstream = new TcpClient();
            await _upstream.ConnectAsync(_broker.Host, _broker.Port, _stopping.Token).ConfigureAwait(false);

            _pumps.Add(PumpToBrokerAsync(_client.GetStream()));
            _pumps.Add(PumpToClientAsync(_client.GetStream(), _upstream.GetStream()));
        }
        catch (Exception ex) when (IsTeardown(ex))
        {
            // Disposed before a client connected.
        }
    }

    private async Task PumpToBrokerAsync(NetworkStream fromClient)
    {
        var ct = _stopping.Token;
        try
        {
            var header = new byte[ProtocolHeaderLength];
            await fromClient.ReadExactlyAsync(header, ct).ConfigureAwait(false);
            await WriteToBrokerAsync(header).ConfigureAwait(false);

            var frameHeader = new byte[FrameHeaderLength];
            while (!ct.IsCancellationRequested)
            {
                await fromClient.ReadExactlyAsync(frameHeader, ct).ConfigureAwait(false);
                var size = BinaryPrimitives.ReadInt32BigEndian(frameHeader.AsSpan(3, 4));

                var frame = new byte[FrameHeaderLength + size + 1];
                frameHeader.CopyTo(frame, 0);
                await fromClient.ReadExactlyAsync(frame.AsMemory(FrameHeaderLength), ct).ConfigureAwait(false);

                var pattern = Volatile.Read(ref _holdPattern);
                if (pattern is not null && frame.AsSpan().IndexOf(pattern) >= 0
                    && Interlocked.CompareExchange(ref _holdPattern, null, pattern) == pattern)
                {
                    _heldFrame = frame;
                    _held.TrySetResult();
                    continue;
                }

                await WriteToBrokerAsync(frame).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsTeardown(ex))
        {
            // One side closed the connection: the relay has nothing left to carry.
        }
    }

    private async Task PumpToClientAsync(NetworkStream toClient, NetworkStream fromBroker)
    {
        try
        {
            await fromBroker.CopyToAsync(toClient, _stopping.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTeardown(ex))
        {
            // One side closed the connection: the relay has nothing left to carry.
        }
    }

    private async Task WriteToBrokerAsync(byte[] bytes)
    {
        await _toBrokerLock.WaitAsync(_stopping.Token).ConfigureAwait(false);
        try
        {
            await _upstream!.GetStream().WriteAsync(bytes, _stopping.Token).ConfigureAwait(false);
        }
        finally
        {
            _toBrokerLock.Release();
        }
    }

    private static bool IsTeardown(Exception ex)
        => ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException or EndOfStreamException;

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        _client?.Dispose();
        _upstream?.Dispose();
        _listener.Stop();

        await _accepting.ConfigureAwait(false);
        await Task.WhenAll(_pumps).ConfigureAwait(false);

        _listener.Dispose();
        _toBrokerLock.Dispose();
        _stopping.Dispose();
    }
}
