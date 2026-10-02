using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Pragmatic.Email.Tests.Unit.Smtp;

/// <summary>
///     Minimal in-process SMTP server for transport/pool tests: greets, answers EHLO with a small
///     capability list and acknowledges every other command with 250. No TLS, no auth.
/// </summary>
internal sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public FakeSmtpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            await WriteAsync(stream, "220 fake ESMTP\r\n").ConfigureAwait(false);

            var buffer = new byte[4096];
            while (!_cts.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                if (read == 0)
                    return;

                var command = Encoding.UTF8.GetString(buffer, 0, read);
                if (command.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                    await WriteAsync(stream, "250-fake\r\n250 PIPELINING\r\n").ConfigureAwait(false);
                else if (command.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    return;
                else
                    await WriteAsync(stream, "250 OK\r\n").ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteAsync(NetworkStream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}
