using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;
using Pragmatic.Notifications.Webhook;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Notifications.Tests.Unit;

/// <summary>
///     <c>WebhookOptions.Timeout</c> (#744): a delivery to an endpoint slower than it fails at it, and one
///     to an endpoint inside it succeeds.
/// </summary>
/// <remarks>
///     A real socket on loopback that answers after a delay the test chooses, so the endpoint is slower (or
///     faster) than the timeout on every run rather than when the machine happens to be. Through the
///     container <c>AddWebhook</c> builds, because the timeout is applied to the named client there.
/// </remarks>
public sealed class AWebhookFailsAtItsConfiguredTimeoutTests
{
    [Fact]
    public async Task AnEndpointSlowerThanTheTimeout_FailsAtTheTimeout()
    {
        await using var endpoint = SlowEndpoint.Start(answerAfter: TimeSpan.FromSeconds(10));
        var channel = Channel(timeout: TimeSpan.FromMilliseconds(300));

        var clock = Stopwatch.StartNew();
        var result = await channel.DeliverAsync(Recipient(endpoint.Url), Content());
        clock.Stop();

        result.Success.Should().BeFalse();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "the delivery ends at the configured 300 ms, not at the 30 s default nor when the endpoint answers");
    }

    /// <summary>The control: an endpoint that answers inside the timeout is delivered to.</summary>
    [Fact]
    public async Task AnEndpointInsideTheTimeout_IsDeliveredTo()
    {
        await using var endpoint = SlowEndpoint.Start(answerAfter: TimeSpan.FromMilliseconds(100));
        var channel = Channel(timeout: TimeSpan.FromSeconds(5));

        var result = await channel.DeliverAsync(Recipient(endpoint.Url), Content());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void TheDefault_IsThirtySeconds()
        => new WebhookOptions().Timeout.Should().Be(TimeSpan.FromSeconds(30));

    private static INotificationChannel Channel(TimeSpan timeout)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddPragmaticNotifications(n => n.AddWebhook(o =>
        {
            o.Timeout = timeout;
            // The endpoint is on loopback, which the SSRF guard refuses by default.
            o.AllowPrivateNetworks = true;
        }));

        return services.BuildServiceProvider()
            .GetRequiredService<INotificationChannelFactory>()
            .GetChannel(NotificationChannel.Webhook)!;
    }

    private static ResolvedRecipient Recipient(string url) => new(url, NotificationChannel.Webhook, null, null, null);

    private static NotificationContent Content() => new() { Subject = "Alert", Body = "Server down" };

    /// <summary>An HTTP endpoint on loopback that reads a request and answers 200 after a fixed delay.</summary>
    private sealed class SlowEndpoint : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;

        private SlowEndpoint(TimeSpan answerAfter)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _serving = ServeAsync(answerAfter);
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/hook";

        public static SlowEndpoint Start(TimeSpan answerAfter) => new(answerAfter);

        /// <remarks>Ends by cancellation when the endpoint is disposed, which is why nobody awaits its outcome.</remarks>
        private async Task ServeAsync(TimeSpan answerAfter)
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                var buffer = new byte[8192];
                _ = await stream.ReadAsync(buffer, _stop.Token).ConfigureAwait(false);
                await Task.Delay(answerAfter, _stop.Token).ConfigureAwait(false);
                await stream.WriteAsync(
                    Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),
                    _stop.Token).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            _listener.Stop();

            // The serving loop ends in the cancellation (or the socket error of a stopped listener) this
            // asked for: waiting for it to stop is the point, its exception is the expected one.
            await _serving.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            _stop.Dispose();
        }
    }
}
