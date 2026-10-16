using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Extensions;
using Pragmatic.Notifications.Webhook;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     The webhook channel (Pragmatic.Notifications.Webhook) POSTs notification content as JSON to
///     the recipient's URL. It enforces an SSRF allowlist via WebhookOptions.AllowedHosts: when the
///     list is non-empty, only matching hosts are delivered. This sample spins up a throwaway local
///     HTTP sink, registers the real WebhookChannel through DI, and shows both an allowed delivery
///     (captured by the sink) and a blocked one (host not in the allowlist).
/// </summary>
public static class WebhookChannelSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Webhook channel (real HTTP POST to local sink) ---");

        // The sink binds a loopback port the system picks, and the URL is made from the port it got.
        using var sink = WebhookSink.Start();
        var url = $"http://127.0.0.1:{sink.Port}/hook";

        var services = new ServiceCollection();
        services.AddLogging();
        // The sink is on loopback, which the SSRF guard refuses by default even when the host is in the
        // allowlist — the two checks are independent. AllowPrivateNetworks says this network is
        // trusted; without it the "allowed" delivery below failed, and the sample printed FAIL.
        services.AddPragmaticNotifications(n => n
            .AddWebhook(o =>
            {
                o.AllowedHosts = ["127.0.0.1"];   // SSRF allowlist
                o.AllowPrivateNetworks = true;    // the local sink is a private address
            })
            .UseInMemoryStore());

        await using var provider = services.BuildServiceProvider();
        var notifications = provider.GetRequiredService<INotificationService>();

        var content = new NotificationContent { Subject = "deploy.completed", Body = "build #4521 is live" };

        // Allowed: host 127.0.0.1 is in the allowlist → delivered and captured by the sink.
        var ok = await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.System,
            Recipient = NotificationRecipient.ToWebhook(url),
            Content = content,
        });
        var received = await sink.WaitForBodyAsync(TimeSpan.FromSeconds(5));

        Console.WriteLine($"  allowed delivery  : {(ok.Success ? "OK" : "FAIL")}");
        Console.WriteLine($"  sink received body: {received ?? "(none)"}");

        // Blocked: evil.example.com is not in AllowedHosts → channel refuses to call out (SSRF guard).
        var blocked = await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.System,
            Recipient = NotificationRecipient.ToWebhook("https://evil.example.com/steal"),
            Content = content,
        });
        var blockedError = blocked.Errors is { Count: > 0 } e ? e[0] : null;
        Console.WriteLine($"  blocked delivery  : success={blocked.Success}  error=\"{blockedError}\"");
        Console.WriteLine();
    }

    /// <summary>Minimal one-shot HTTP sink that records the body of the first request it receives.</summary>
    /// <remarks>
    ///     A <see cref="TcpListener" /> on port 0 and not an <c>HttpListener</c>: the listener binds the port
    ///     the system gives it and keeps it. An <c>HttpListener</c> needs its port before it starts, and a
    ///     port chosen first and bound after is free in between for anything else on the machine to take
    ///     (#259). It reads one request whose body has a <c>Content-Length</c>, which is what the webhook
    ///     channel sends, and answers 200.
    /// </remarks>
    private sealed class WebhookSink : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly TaskCompletionSource<string> _body = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private WebhookSink() => _listener.Start();

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static WebhookSink Start()
        {
            var sink = new WebhookSink();
            _ = Task.Run(sink.AcceptOneAsync);
            return sink;
        }

        private async Task AcceptOneAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

                var contentLength = 0;
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { Length: > 0 } header)
                {
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        contentLength = int.Parse(header["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                }

                var body = new char[contentLength];
                var read = 0;
                while (read < contentLength
                       && await reader.ReadAsync(body.AsMemory(read)).ConfigureAwait(false) is > 0 and var n)
                    read += n;
                _body.TrySetResult(new string(body, 0, read));

                var response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();
                await stream.WriteAsync(response).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or IOException)
            {
                // Listener stopped before a request arrived — nothing to record.
            }
        }

        public async Task<string?> WaitForBodyAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_body.Task, Task.Delay(timeout)).ConfigureAwait(false);
            return ReferenceEquals(completed, _body.Task) ? _body.Task.Result : null;
        }

        public void Dispose() => _listener.Stop();
    }
}
