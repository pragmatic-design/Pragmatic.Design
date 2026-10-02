using System.Net;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Notifications.Slack;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class SlackChannelTests
{
    private static (SlackChannel Channel, RecordingHandler Handler) Create(
        SlackOptions options, HttpStatusCode status = HttpStatusCode.OK, string body = "ok")
    {
        var handler = new RecordingHandler(status, body);
        var factory = new HttpClientFactoryMock();
        factory.CreateClient.When(SlackChannel.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var channel = new SlackChannel(factory, Options.Create(options), NullLogger<SlackChannel>.Instance);
        return (channel, handler);
    }

    private static ResolvedRecipient Recipient(string address)
        => new(address, NotificationChannel.Slack, null, null, null);

    private static NotificationContent Content(string subject = "Alert", string body = "Something happened")
        => new() { Subject = subject, Body = body };

    [Fact]
    public void Channel_IsSlack()
    {
        var (channel, _) = Create(new SlackOptions());

        channel.Channel.Should().Be(NotificationChannel.Slack);
    }

    [Fact]
    public async Task DeliverAsync_PostsSubjectAndBody()
    {
        var (channel, handler) = Create(new SlackOptions());

        var result = await channel.DeliverAsync(
            Recipient("https://hooks.slack.com/services/T000/B000/xxx"), Content());

        result.Success.Should().BeTrue();
        handler.LastBody.Should().Contain("Alert").And.Contain("Something happened");
        handler.LastRequestUri!.Host.Should().Be("hooks.slack.com");
    }

    [Fact]
    public async Task DeliverAsync_WithoutRecipientUrl_UsesTheDefault()
    {
        var (channel, handler) = Create(new SlackOptions
        {
            DefaultWebhookUrl = "https://hooks.slack.com/services/T000/B000/default",
        });

        var result = await channel.DeliverAsync(Recipient("ops-channel"), Content());

        result.Success.Should().BeTrue();
        handler.LastRequestUri!.AbsolutePath.Should().EndWith("default");
    }

    [Fact]
    public async Task DeliverAsync_WithNoUrlAnywhere_FailsWithAnActionableError()
    {
        var (channel, handler) = Create(new SlackOptions());

        var result = await channel.DeliverAsync(Recipient("ops-channel"), Content());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("DefaultWebhookUrl");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DeliverAsync_NonAllowedHost_IsBlocked()
    {
        // The address comes from application data, so it is untrusted: a crafted one must not turn
        // notification delivery into a request against an arbitrary endpoint.
        var (channel, handler) = Create(new SlackOptions());

        var result = await channel.DeliverAsync(Recipient("https://evil.example.com/hook"), Content());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("allowed host");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DeliverAsync_PlainHttpUrl_IsBlocked()
    {
        var (channel, handler) = Create(new SlackOptions());

        var result = await channel.DeliverAsync(Recipient("http://hooks.slack.com/services/x"), Content());

        result.Success.Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DeliverAsync_CustomAllowedHost_IsAccepted()
    {
        var (channel, handler) = Create(new SlackOptions { AllowedHosts = ["slack.internal.example"] });

        var result = await channel.DeliverAsync(Recipient("https://slack.internal.example/hook"), Content());

        result.Success.Should().BeTrue();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task DeliverAsync_ErrorResponse_ReportsTheStatusAndReason()
    {
        var (channel, _) = Create(new SlackOptions(), HttpStatusCode.BadRequest, "invalid_payload");

        var result = await channel.DeliverAsync(
            Recipient("https://hooks.slack.com/services/T000/B000/xxx"), Content());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("400").And.Contain("invalid_payload");
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? LastBody { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
