using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Pragmatic.Notifications.Tests.Unit;

/// <summary>
///     A webhook receiver on loopback that answers each request with the next status of a script, and keeps
///     every request it was sent — headers and body — for the test to verify as a receiver would.
/// </summary>
internal sealed class ScriptedWebhookEndpoint : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Queue<int> _statuses;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serving;

    private ScriptedWebhookEndpoint(IEnumerable<int> statuses)
    {
        _statuses = new Queue<int>(statuses);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _serving = ServeAsync();
    }

    /// <summary>The requests received, in order.</summary>
    public List<ReceivedWebhook> Received { get; } = [];

    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/hook";

    /// <summary>Answers the n-th request with the n-th status, and 200 once the script is spent.</summary>
    public static ScriptedWebhookEndpoint Answering(params int[] statuses) => new(statuses);

    /// <remarks>Ends by cancellation when the endpoint is disposed, which is why nobody awaits its outcome.</remarks>
    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            Received.Add(await ReadAsync(stream, _stop.Token).ConfigureAwait(false));

            var status = _statuses.Count > 0 ? _statuses.Dequeue() : 200;
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Scripted\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),
                _stop.Token).ConfigureAwait(false);
        }
    }

    /// <summary>Reads one request: the headers up to the blank line, then as much body as Content-Length says.</summary>
    private static async Task<ReceivedWebhook> ReadAsync(NetworkStream stream, CancellationToken ct)
    {
        var bytes = new List<byte>();
        var buffer = new byte[4096];
        int headerEnd;
        while ((headerEnd = IndexOfBlankLine(bytes)) < 0)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;
            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        var head = Encoding.ASCII.GetString([.. bytes.Take(Math.Max(headerEnd, 0))]);
        var headers = head.Split("\r\n").Skip(1)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

        var length = headers.TryGetValue("Content-Length", out var declared) ? int.Parse(declared, System.Globalization.CultureInfo.InvariantCulture) : 0;
        var body = bytes.Skip(headerEnd + 4).ToList();
        while (body.Count < length)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;
            body.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        return new ReceivedWebhook(headers, Encoding.UTF8.GetString([.. body]));
    }

    private static int IndexOfBlankLine(List<byte> bytes)
    {
        for (var i = 0; i + 3 < bytes.Count; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
                return i;
        }

        return -1;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();

        // The serving loop ends in the cancellation (or the socket error of a stopped listener) this asked
        // for: waiting for it to stop is the point, its exception is the expected one.
        await _serving.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stop.Dispose();
    }
}
