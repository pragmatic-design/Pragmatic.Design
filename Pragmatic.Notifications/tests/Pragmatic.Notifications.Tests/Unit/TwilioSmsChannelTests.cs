using System.Net;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Notifications.Sms;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class TwilioSmsChannelTests
{
    private static TwilioSmsOptions DefaultOptions() => new()
    {
        AccountSid = "AC123",
        AuthToken = "secret-token",
        FromNumber = "+15550000000",
    };

    private static (TwilioSmsChannel Channel, RecordingHandler Handler) Create(
        TwilioSmsOptions? options = null, HttpStatusCode status = HttpStatusCode.Created)
    {
        var handler = new RecordingHandler(status);
        var factory = new HttpClientFactoryMock();
        factory.CreateClient.When(TwilioSmsChannel.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var channel = new TwilioSmsChannel(
            factory, Options.Create(options ?? DefaultOptions()), NullLogger<TwilioSmsChannel>.Instance);

        return (channel, handler);
    }

    private static ResolvedRecipient Recipient(string address = "+15551234567")
        => new(address, NotificationChannel.Sms, null, null, null);

    [Fact]
    public void Channel_IsSms()
    {
        var (channel, _) = Create();

        channel.Channel.Should().Be(NotificationChannel.Sms);
    }

    [Fact]
    public async Task DeliverAsync_PostsToTheTwilioMessagesEndpoint()
    {
        var (channel, handler) = Create();

        var result = await channel.DeliverAsync(
            Recipient(), new NotificationContent { Subject = "Booking", Body = "Confirmed" });

        result.Success.Should().BeTrue();
        handler.LastRequestUri!.AbsolutePath.Should().Be("/2010-04-01/Accounts/AC123/Messages.json");
        handler.LastBody.Should().Contain("To=%2B15551234567");
        handler.LastBody.Should().Contain("From=%2B15550000000");
    }

    [Fact]
    public async Task DeliverAsync_SendsBasicAuthentication()
    {
        var (channel, handler) = Create();

        await channel.DeliverAsync(Recipient(), new NotificationContent { Subject = "S", Body = "B" });

        handler.LastAuthorization!.Scheme.Should().Be("Basic");
        var decoded = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(handler.LastAuthorization.Parameter!));
        decoded.Should().Be("AC123:secret-token");
    }

    [Fact]
    public async Task DeliverAsync_PrefersShortBody()
    {
        // ShortBody exists precisely for length-limited channels, so SMS must read it.
        var (channel, handler) = Create();

        await channel.DeliverAsync(Recipient(), new NotificationContent
        {
            Subject = "A very long subject that would waste characters",
            Body = "A long body meant for e-mail",
            ShortBody = "Booking confirmed",
        });

        handler.LastBody.Should().Contain("Body=Booking+confirmed");
        handler.LastBody.Should().NotContain("waste");
    }

    [Fact]
    public async Task DeliverAsync_WithoutShortBody_CombinesSubjectAndBody()
    {
        var (channel, handler) = Create();

        await channel.DeliverAsync(Recipient(), new NotificationContent { Subject = "Booking", Body = "Confirmed" });

        handler.LastBody.Should().Contain("Booking%3A+Confirmed");
    }

    [Fact]
    public async Task DeliverAsync_EmptyRecipient_FailsWithoutCallingTheProvider()
    {
        var (channel, handler) = Create();

        var result = await channel.DeliverAsync(
            Recipient("  "), new NotificationContent { Subject = "S", Body = "B" });

        result.Success.Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DeliverAsync_ProviderError_DoesNotLeakTheResponseBody()
    {
        // Twilio echoes the request parameters, phone numbers included, so the body must not surface.
        var (channel, _) = Create(status: HttpStatusCode.BadRequest);

        var result = await channel.DeliverAsync(
            Recipient(), new NotificationContent { Subject = "S", Body = "B" });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("400");
        result.ErrorMessage.Should().NotContain("15551234567");
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("short", 1)]
    [InlineData("x", 1)]
    public void SegmentCount_CountsBillableSegments(string body, int expected)
    {
        TwilioSmsChannel.SegmentCount(body).Should().Be(expected);
    }

    [Fact]
    public void SegmentCount_LongBody_SplitsAt160Characters()
    {
        TwilioSmsChannel.SegmentCount(new string('x', 160)).Should().Be(1);
        TwilioSmsChannel.SegmentCount(new string('x', 161)).Should().Be(2);
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? LastBody { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public System.Net.Http.Headers.AuthenticationHeaderValue? LastAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent("""{"sid":"SM1","to":"+15551234567"}"""),
            };
        }
    }
}
