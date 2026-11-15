using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Pragmatic.Client.Tests.Cli;

/// <summary>
///     An <c>[Sse]</c> endpoint on loopback: answers every request with a <c>text/event-stream</c> body made of
///     the frames it was given, written the way <c>SseStreamResult</c> writes them, then closes.
/// </summary>
internal sealed class SseLoopbackEndpoint : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly string[] _frames;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serving;

    private SseLoopbackEndpoint(string[] frames)
    {
        _frames = frames;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _serving = ServeAsync();
    }

    public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    /// <summary>The request line of every request received, e.g. <c>GET /links/watch?tag=news HTTP/1.1</c>.</summary>
    public List<string> RequestLines { get; } = [];

    /// <summary>Serves these frames, each followed by the blank line that ends an event.</summary>
    public static SseLoopbackEndpoint Streaming(params string[] frames) => new(frames);

    /// <remarks>Ends by cancellation when the endpoint is disposed, which is why nobody awaits its outcome.</remarks>
    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            var stream = client.GetStream();

            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer, _stop.Token).ConfigureAwait(false);
            RequestLines.Add(Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0]);

            await WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
            foreach (var frame in _frames)
                await WriteAsync(stream, frame + "\n\n").ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(NetworkStream stream, string text)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), _stop.Token).ConfigureAwait(false);
        await stream.FlushAsync(_stop.Token).ConfigureAwait(false);
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
