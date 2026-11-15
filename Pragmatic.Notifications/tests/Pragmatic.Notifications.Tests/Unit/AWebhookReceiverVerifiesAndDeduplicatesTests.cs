using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;
using Pragmatic.Notifications.Webhook;
using Pragmatic.Resilience;
using Pragmatic.Resilience.Strategies;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Notifications.Tests.Unit;

/// <summary>
///     A webhook receiver (#745) verifies each delivery with its own secret, and sees the same delivery id on
///     every attempt of a delivery the channel retried.
/// </summary>
/// <remarks>
///     A real receiver on loopback that answers 500 and then 200, so the delivery is retried on every run;
///     the receiver's side is verified as a receiver would: HMAC-SHA256 over
///     <c>"{timestamp}.{deliveryId}.{body}"</c> with the secret it holds. Through the container
///     <c>AddWebhook</c> and <c>AddPragmaticResilience</c> build.
/// </remarks>
public sealed class AWebhookReceiverVerifiesAndDeduplicatesTests
{
    private const string ApplicationSecret = "the-application-secret";
    private const string DestinationSecret = "this-receiver-s-own-secret";

    [Fact]
    public async Task ARetriedDelivery_IsSignedWithTheReceiversSecret_AndCarriesOneDeliveryId()
    {
        await using var receiver = ScriptedWebhookEndpoint.Answering(500, 200);
        var channel = Channel(policy: "webhooks", destinationSecret: DestinationSecret);

        var result = await channel.DeliverAsync(Recipient(receiver.Url), Content());

        result.Success.Should().BeTrue("the second attempt was answered 200");
        receiver.Received.Should().HaveCount(2, "the 500 was retried");

        var ids = receiver.Received.Select(r => r.Headers["X-Pragmatic-Delivery"]).ToList();
        ids.Should().AllSatisfy(id => id.Should().NotBeNullOrEmpty());
        ids.Distinct().Should().HaveCount(1, "a redelivery carries the id of the delivery, not a new one");

        receiver.Received.Should().AllSatisfy(r => Verifies(r, DestinationSecret).Should().BeTrue(
            "the receiver verifies with the secret it was given"));
        receiver.Received.Should().AllSatisfy(r => Verifies(r, ApplicationSecret).Should().BeFalse(
            "and not with the application's"));
    }

    /// <summary>A destination with no secret of its own is signed with the application's.</summary>
    [Fact]
    public async Task ADestinationWithNoSecretOfItsOwn_IsSignedWithTheApplicationsSecret()
    {
        await using var receiver = ScriptedWebhookEndpoint.Answering(200);
        var channel = Channel(policy: null, destinationSecret: null);

        (await channel.DeliverAsync(Recipient(receiver.Url), Content())).Success.Should().BeTrue();

        Verifies(receiver.Received.Single(), ApplicationSecret).Should().BeTrue();
    }

    /// <summary>The control for the retry: with no policy, one attempt, and the 500 is the delivery's failure.</summary>
    [Fact]
    public async Task WithoutAPolicy_ThereIsOneAttempt()
    {
        await using var receiver = ScriptedWebhookEndpoint.Answering(500, 200);
        var channel = Channel(policy: null, destinationSecret: DestinationSecret);

        var result = await channel.DeliverAsync(Recipient(receiver.Url), Content());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("HTTP 500");
        receiver.Received.Should().HaveCount(1);
    }

    /// <summary>A delivery the policy gives up on reports the receiver's last status.</summary>
    [Fact]
    public async Task ADeliveryTheRetryGivesUpOn_ReportsTheLastStatus()
    {
        await using var receiver = ScriptedWebhookEndpoint.Answering(503, 503, 503);
        var channel = Channel(policy: "webhooks", destinationSecret: null);

        var result = await channel.DeliverAsync(Recipient(receiver.Url), Content());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("HTTP 503");
        receiver.Received.Should().HaveCount(3, "one attempt and the policy's two retries");
    }

    /// <summary>What a receiver does: recompute the HMAC over what it was sent, with the secret it holds.</summary>
    private static bool Verifies(ReceivedWebhook request, string secret)
    {
        var timestamp = request.Headers["X-Pragmatic-Timestamp"];
        var deliveryId = request.Headers["X-Pragmatic-Delivery"];
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = "sha256=" + Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{deliveryId}.{request.Body}"))).ToLowerInvariant();

        return request.Headers["X-Pragmatic-Signature"] == expected;
    }

    private static INotificationChannel Channel(string? policy, string? destinationSecret)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddPragmaticResilience();
        services.AddResiliencePolicy("webhooks", p => p.Retry = new RetryOptions
        {
            MaxRetries = 2,
            BaseDelay = TimeSpan.FromMilliseconds(10),
            UseJitter = false,
        });
        services.AddPragmaticNotifications(n => n.AddWebhook(o =>
        {
            o.SigningSecret = ApplicationSecret;
            o.ResiliencePolicy = policy;
            // The receiver is on loopback, which the SSRF guard refuses by default.
            o.AllowPrivateNetworks = true;
        }));
        if (destinationSecret is not null)
            services.AddSingleton<IWebhookSigningSecretResolver>(new OneSecret(destinationSecret));

        return services.BuildServiceProvider()
            .GetRequiredService<INotificationChannelFactory>()
            .GetChannel(NotificationChannel.Webhook)!;
    }

    private static ResolvedRecipient Recipient(string url) => new(url, NotificationChannel.Webhook, null, null, null);

    private static NotificationContent Content() => new() { Subject = "Invoice issued", Body = "INV-1" };

    private sealed class OneSecret(string secret) : IWebhookSigningSecretResolver
    {
        public ValueTask<string?> ResolveAsync(Uri destination, string? tenantId, CancellationToken ct = default)
            => ValueTask.FromResult<string?>(secret);
    }
}
