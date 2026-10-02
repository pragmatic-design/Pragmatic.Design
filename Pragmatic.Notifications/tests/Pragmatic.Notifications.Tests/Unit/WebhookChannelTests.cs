using System.Net;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Notifications.Webhook;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class WebhookChannelTests
{
    // These tests exercise delivery mechanics with a mocked handler, not the SSRF guard, so they allow
    // private networks (otherwise the guard would do a real DNS lookup on the test host). The SSRF guard
    // itself is covered by DeliverAsync_PrivateAddress_IsBlocked below.
    private static readonly IOptions<WebhookOptions> DefaultOptions =
        Options.Create(new WebhookOptions { AllowPrivateNetworks = true });

    /// <summary>The production default — the SSRF guard active.</summary>
    private static readonly IOptions<WebhookOptions> BlockingOptions =
        Options.Create(new WebhookOptions { AllowPrivateNetworks = false });

    [Fact]
    public async Task DeliverAsync_WithSuccessfulPost_ReturnsSuccess()
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, "ok");
        var factory = CreateFactory(handler);
        var channel = new WebhookChannel(factory, DefaultOptions, NullLogger<WebhookChannel>.Instance);
        var recipient = new ResolvedRecipient("https://hooks.example.com/test", NotificationChannel.Webhook, null, null, null);
        var content = new NotificationContent { Subject = "Alert", Body = "Server down" };

        var result = await channel.DeliverAsync(recipient, content);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeliverAsync_WithFailedPost_ReturnsFailed()
    {
        var handler = new MockHttpHandler(HttpStatusCode.InternalServerError, "error");
        var factory = CreateFactory(handler);
        var channel = new WebhookChannel(factory, DefaultOptions, NullLogger<WebhookChannel>.Instance);
        var recipient = new ResolvedRecipient("https://hooks.example.com/test", NotificationChannel.Webhook, null, null, null);
        var content = new NotificationContent { Subject = "Alert", Body = "Server down" };

        var result = await channel.DeliverAsync(recipient, content);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("500");
    }

    [Fact]
    public async Task DeliverAsync_WithJsonPayload_SendsCustomPayload()
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, "ok");
        var factory = CreateFactory(handler);
        var channel = new WebhookChannel(factory, DefaultOptions, NullLogger<WebhookChannel>.Instance);
        var recipient = new ResolvedRecipient("https://hooks.example.com/test", NotificationChannel.Webhook, null, null, null);
        var content = new NotificationContent
        {
            Subject = "Alert",
            Body = "Server down",
            JsonPayload = """{"custom":"payload","severity":"critical"}""",
        };

        var result = await channel.DeliverAsync(recipient, content);

        result.Success.Should().BeTrue();
        handler.LastContent.Should().Contain("custom");
    }

    [Fact]
    public async Task DeliverAsync_PrivateAddress_IsBlocked()
    {
        // SSRF guard: a loopback target must be refused before any HTTP call when private networks
        // are not allowed (the default).
        var handler = new MockHttpHandler(HttpStatusCode.OK, "ok");
        var factory = CreateFactory(handler);
        var options = Options.Create(new WebhookOptions { AllowPrivateNetworks = false });
        var channel = new WebhookChannel(factory, options, NullLogger<WebhookChannel>.Instance);
        var recipient = new ResolvedRecipient("http://127.0.0.1/internal", NotificationChannel.Webhook, null, null, null);
        var content = new NotificationContent { Subject = "x", Body = "y" };

        var result = await channel.DeliverAsync(recipient, content);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("private or loopback");
    }

    // The addresses below were the gap between WebhookChannel's private SSRF check and the canonical
    // OutboundUrlGuard: each one the old copy let through and the guard refuses. They are literals, so
    // no DNS is involved and the assertion is about the range table, not about name resolution.
    [Theory]
    [InlineData("http://0.0.0.0/hook")]          // "this network" — reaches localhost on Linux
    [InlineData("http://0.1.2.3/hook")]          // rest of 0.0.0.0/8
    [InlineData("http://[::]/hook")]             // IPv6 unspecified, same trick
    [InlineData("http://100.64.0.1/hook")]       // carrier-grade NAT
    [InlineData("http://224.0.0.1/hook")]        // multicast
    [InlineData("http://255.255.255.255/hook")]  // broadcast
    public async Task DeliverAsync_AddressTheOldPrivateCopyMissed_IsBlocked(string address)
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, "ok");
        var channel = new WebhookChannel(CreateFactory(handler), BlockingOptions, NullLogger<WebhookChannel>.Instance);
        var recipient = new ResolvedRecipient(address, NotificationChannel.Webhook, null, null, null);

        var result = await channel.DeliverAsync(recipient, new NotificationContent { Subject = "x", Body = "y" });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("private or loopback");
        handler.LastContent.Should().BeNull("the request must never leave the process");
    }

    [Fact]
    public async Task DeliverAsync_CredentialsInUrl_IsBlocked()
    {
        // https://trusted.example.com@attacker.test/ reads as the trusted host to a person and resolves
        // to the attacker's for a machine, so a URL carrying UserInfo is refused.
        var handler = new MockHttpHandler(HttpStatusCode.OK, "ok");
        var channel = new WebhookChannel(CreateFactory(handler), BlockingOptions, NullLogger<WebhookChannel>.Instance);
        var recipient = new ResolvedRecipient(
            "https://hooks.example.com@attacker.test/hook", NotificationChannel.Webhook, null, null, null);

        var result = await channel.DeliverAsync(recipient, new NotificationContent { Subject = "x", Body = "y" });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("credentials");
        handler.LastContent.Should().BeNull();
    }

    [Fact]
    public async Task DeliverAsync_NonHttpScheme_IsBlocked()
    {
        // file:// reads the server's disk; the guard refuses anything that is not http(s).
        var handler = new MockHttpHandler(HttpStatusCode.OK, "ok");
        var channel = new WebhookChannel(CreateFactory(handler), BlockingOptions, NullLogger<WebhookChannel>.Instance);
        var recipient = new ResolvedRecipient("file:///etc/passwd", NotificationChannel.Webhook, null, null, null);

        var result = await channel.DeliverAsync(recipient, new NotificationContent { Subject = "x", Body = "y" });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("absolute http(s) URL");
    }

    [Fact]
    public async Task DeliverAsync_CredentialsInUrl_IsBlockedEvenWhenPrivateNetworksAllowed()
    {
        // AllowPrivateNetworks waives the address check only. It is about trusting the network, not
        // about accepting any URL at all, so scheme and credentials still apply.
        var handler = new MockHttpHandler(HttpStatusCode.OK, "ok");
        var channel = new WebhookChannel(CreateFactory(handler), DefaultOptions, NullLogger<WebhookChannel>.Instance);
        var recipient = new ResolvedRecipient(
            "https://hooks.example.com@attacker.test/hook", NotificationChannel.Webhook, null, null, null);

        var result = await channel.DeliverAsync(recipient, new NotificationContent { Subject = "x", Body = "y" });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("credentials");
    }

    [Fact]
    public async Task DeliverAsync_PrivateAddress_IsDeliveredWhenPrivateNetworksAllowed()
    {
        // The escape hatch still works: an operator who set AllowPrivateNetworks gets the loopback call.
        var handler = new MockHttpHandler(HttpStatusCode.OK, "ok");
        var channel = new WebhookChannel(CreateFactory(handler), DefaultOptions, NullLogger<WebhookChannel>.Instance);
        var recipient = new ResolvedRecipient("http://127.0.0.1/internal", NotificationChannel.Webhook, null, null, null);

        var result = await channel.DeliverAsync(recipient, new NotificationContent { Subject = "x", Body = "y" });

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Channel_ReturnsWebhook()
    {
        var factory = new HttpClientFactoryMock();
        var channel = new WebhookChannel(factory, DefaultOptions, NullLogger<WebhookChannel>.Instance);

        channel.Channel.Should().Be(NotificationChannel.Webhook);
    }

    private static IHttpClientFactory CreateFactory(HttpMessageHandler handler)
    {
        var factory = new HttpClientFactoryMock();
        factory.CreateClient.When("Pragmatic.Notifications.Webhook").Returns(new HttpClient(handler));
        return factory;
    }

    private sealed class MockHttpHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        public string? LastContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                LastContent = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(statusCode) { Content = new StringContent(content) };
        }
    }
}
