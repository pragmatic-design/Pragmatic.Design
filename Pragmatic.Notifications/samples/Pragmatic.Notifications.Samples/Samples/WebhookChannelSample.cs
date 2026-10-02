using System.Net;
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
///     HTTP sink (System.Net.HttpListener), registers the real WebhookChannel through DI, and shows
///     both an allowed delivery (captured by the sink) and a blocked one (host not in the allowlist).
/// </summary>
public static class WebhookChannelSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Webhook channel (real HTTP POST to local sink) ---");

        // Local sink on a loopback port. HttpListener requires the trailing slash.
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var sink = new WebhookSink(prefix);
        sink.Start();

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
            Recipient = NotificationRecipient.ToWebhook($"{prefix}hook"),
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

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Minimal one-shot HTTP sink that records the first request body it receives.</summary>
    private sealed class WebhookSink(string prefix) : IDisposable
    {
        private readonly HttpListener _listener = CreateListener(prefix);
        private readonly TaskCompletionSource<string> _body = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static HttpListener CreateListener(string prefix)
        {
            var l = new HttpListener();
            l.Prefixes.Add(prefix);
            return l;
        }

        public void Start()
        {
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                _body.TrySetResult(body);

                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                // Listener stopped — ignore.
            }
        }

        public async Task<string?> WaitForBodyAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_body.Task, Task.Delay(timeout)).ConfigureAwait(false);
            return ReferenceEquals(completed, _body.Task) ? _body.Task.Result : null;
        }

        public void Dispose()
        {
            if (_listener.IsListening)
                _listener.Stop();
            ((IDisposable)_listener).Dispose();
        }
    }
}
