using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Preferences;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class NotificationPipelineTests
{
    private readonly RecipientResolverMock _resolver = new RecipientResolverMock();
    private readonly NotificationRouterMock _router = new NotificationRouterMock();
    private readonly NotificationChannelFactoryMock _channelFactory = new NotificationChannelFactoryMock();
    private readonly INotificationStore _store;
    private readonly NotificationPipeline _pipeline;

    public NotificationPipelineTests()
    {
        _store = new InMemoryNotificationStore();
        _pipeline = new NotificationPipeline(
            _resolver, _router, _channelFactory, _store,
            NullLogger<NotificationPipeline>.Instance);
    }

    private static NotificationRequest CreateRequest(string subject = "Test", string body = "Body") => new()
    {
        Audience = NotificationAudience.EndUser,
        Recipient = NotificationRecipient.Direct("user@example.com"),
        Content = new NotificationContent { Subject = subject, Body = body },
    };

    [Fact]
    public async Task Execute_WithEmptySubject_ReturnsValidationError()
    {
        var request = CreateRequest(subject: "", body: "Body");

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("subject"));
    }

    [Fact]
    public async Task Execute_WithEmptyBody_ReturnsValidationError()
    {
        var request = CreateRequest(subject: "Test", body: "");

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("body"));
    }

    [Fact]
    public async Task Execute_WithNoResolvedRecipients_ReturnsFailed()
    {
        var request = CreateRequest();
        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>([]));

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("No recipients"));
    }

    [Fact]
    public async Task Execute_WithResolvedRecipient_DeliversViaChannel()
    {
        var request = CreateRequest();
        var resolved = new ResolvedRecipient("user@example.com", NotificationChannel.Email, null, null, null);

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>([resolved]));

        _router.Route.Returns(NotificationChannel.Email);

        var mockChannel = new NotificationChannelMock();
        mockChannel.Channel.Returns(NotificationChannel.Email);
        mockChannel.DeliverAsync.Returns(Task.FromResult(DeliveryResult.Succeeded("msg-123")));

        _channelFactory.GetChannel.When(NotificationChannel.Email, Arg.Any<string?>())
.Returns(mockChannel);

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeTrue();
        result.DeliveryIds.Should().HaveCount(1);
        mockChannel.DeliverAsync.Received(1);
    }

    [Fact]
    public async Task Execute_WithChannelOverride_UsesOverrideChannel()
    {
        var request = CreateRequest() with { ChannelOverride = NotificationChannel.Webhook };
        var resolved = new ResolvedRecipient("https://hooks.example.com", NotificationChannel.Webhook, null, null, null);

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>([resolved]));

        var mockChannel = new NotificationChannelMock();
        mockChannel.Channel.Returns(NotificationChannel.Webhook);
        mockChannel.DeliverAsync.Returns(Task.FromResult(DeliveryResult.Succeeded()));

        _channelFactory.GetChannel.When(NotificationChannel.Webhook, Arg.Any<string?>())
.Returns(mockChannel);

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeTrue();
        // Router should NOT be called when channel is overridden
        _router.Route.DidNotReceive();
    }

    [Fact]
    public async Task Execute_WithDeliveryFailure_TracksFailedStatus()
    {
        var request = CreateRequest();
        var resolved = new ResolvedRecipient("user@example.com", NotificationChannel.Email, null, null, null);

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>([resolved]));

        _router.Route.Returns(NotificationChannel.Email);

        var mockChannel = new NotificationChannelMock();
        mockChannel.Channel.Returns(NotificationChannel.Email);
        mockChannel.DeliverAsync.Returns(Task.FromResult(DeliveryResult.Failed("SMTP connection refused")));

        _channelFactory.GetChannel.When(NotificationChannel.Email, Arg.Any<string?>())
.Returns(mockChannel);

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("SMTP connection refused"));
    }

    [Fact]
    public async Task Execute_WithMultipleRecipients_DeliversToAll()
    {
        var request = CreateRequest();
        var r1 = new ResolvedRecipient("a@example.com", NotificationChannel.Email, null, null, null);
        var r2 = new ResolvedRecipient("b@example.com", NotificationChannel.Email, null, null, null);

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>([r1, r2]));

        _router.Route.Returns(NotificationChannel.Email);

        var mockChannel = new NotificationChannelMock();
        mockChannel.Channel.Returns(NotificationChannel.Email);
        mockChannel.DeliverAsync.Returns(Task.FromResult(DeliveryResult.Succeeded()));

        _channelFactory.GetChannel.When(NotificationChannel.Email, Arg.Any<string?>())
.Returns(mockChannel);

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeTrue();
        result.DeliveryIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task Execute_WithCriticalPriority_AndMultipleChannels_DeliversToAll()
    {
        var request = CreateRequest() with { Priority = NotificationPriority.Critical };
        var resolved = new ResolvedRecipient("user@example.com", NotificationChannel.Email, null, null, null);

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>([resolved]));

        _router.Route.Returns(NotificationChannel.Email | NotificationChannel.Webhook);

        var emailChannel = new NotificationChannelMock();
        emailChannel.Channel.Returns(NotificationChannel.Email);
        emailChannel.DeliverAsync.Returns(Task.FromResult(DeliveryResult.Succeeded()));

        var webhookChannel = new NotificationChannelMock();
        webhookChannel.Channel.Returns(NotificationChannel.Webhook);
        webhookChannel.DeliverAsync.Returns(Task.FromResult(DeliveryResult.Succeeded()));

        _channelFactory.GetChannel.When(NotificationChannel.Email, Arg.Any<string?>()).Returns(emailChannel);
        _channelFactory.GetChannel.When(NotificationChannel.Webhook, Arg.Any<string?>()).Returns(webhookChannel);

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeTrue();
        result.DeliveryIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task Execute_WithTrackingId_SettlesRootRecordToSent()
    {
        var request = CreateRequest();
        var resolved = new ResolvedRecipient("user@example.com", NotificationChannel.Email, null, null, null);

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>([resolved]));
        _router.Route.Returns(NotificationChannel.Email);

        var mockChannel = new NotificationChannelMock();
        mockChannel.Channel.Returns(NotificationChannel.Email);
        mockChannel.DeliverAsync.Returns(Task.FromResult(DeliveryResult.Succeeded()));
        _channelFactory.GetChannel.When(NotificationChannel.Email, Arg.Any<string?>()).Returns(mockChannel);

        var root = await _store.CreateAsync(new NotificationRecord
        {
            Audience = request.Audience,
            RecipientAddress = "user@example.com",
            Channel = NotificationChannel.None,
            Subject = request.Content.Subject,
            Status = DeliveryStatus.Pending,
        });

        var result = await _pipeline.ExecuteAsync(request, root.Id);

        result.Success.Should().BeTrue();
        result.NotificationId.Should().Be(root.Id);
        var settled = await _store.GetByIdAsync(root.Id);
        settled!.Status.Should().Be(DeliveryStatus.Sent, "the EnqueueAsync record must not stay Pending forever");
    }

    [Fact]
    public async Task Execute_WithTrackingId_AndDeliveryFailure_SettlesRootRecordToFailed()
    {
        var request = CreateRequest();
        var resolved = new ResolvedRecipient("user@example.com", NotificationChannel.Email, null, null, null);

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>([resolved]));
        _router.Route.Returns(NotificationChannel.Email);

        var mockChannel = new NotificationChannelMock();
        mockChannel.Channel.Returns(NotificationChannel.Email);
        mockChannel.DeliverAsync.Returns(Task.FromResult(DeliveryResult.Failed("SMTP refused")));
        _channelFactory.GetChannel.When(NotificationChannel.Email, Arg.Any<string?>()).Returns(mockChannel);

        var root = await _store.CreateAsync(new NotificationRecord
        {
            Audience = request.Audience,
            RecipientAddress = "user@example.com",
            Channel = NotificationChannel.None,
            Subject = request.Content.Subject,
            Status = DeliveryStatus.Pending,
        });

        var result = await _pipeline.ExecuteAsync(request, root.Id);

        result.Success.Should().BeFalse();
        var settled = await _store.GetByIdAsync(root.Id);
        settled!.Status.Should().Be(DeliveryStatus.Failed);
        settled.ErrorMessage.Should().Contain("SMTP refused");
    }

    [Fact]
    public async Task Execute_WithPartialFailure_ReportsSuccessWithErrors()
    {
        var request = CreateRequest();
        var ok = new ResolvedRecipient("ok@example.com", NotificationChannel.Email, null, null, null);
        var ko = new ResolvedRecipient("ko@example.com", NotificationChannel.Email, null, null, null);

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>([ok, ko]));
        _router.Route.Returns(NotificationChannel.Email);

        var mockChannel = new NotificationChannelMock();
        mockChannel.Channel.Returns(NotificationChannel.Email);
        mockChannel.DeliverAsync.When(ok, Arg.Any<NotificationContent>(), Arg.Any<CancellationToken>())
.Returns(Task.FromResult(DeliveryResult.Succeeded()));
        mockChannel.DeliverAsync.When(ko, Arg.Any<NotificationContent>(), Arg.Any<CancellationToken>())
.Returns(Task.FromResult(DeliveryResult.Failed("mailbox full")));
        _channelFactory.GetChannel.When(NotificationChannel.Email, Arg.Any<string?>()).Returns(mockChannel);

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeTrue("at least one delivery succeeded");
        result.Errors.Should().NotBeNullOrEmpty("partial failures must not be masked as a clean success");
        result.Errors.Should().Contain(e => e.Contains("mailbox full"));
    }

    [Fact]
    public async Task Execute_WithChannelException_TracksFailedAndContinues()
    {
        var request = CreateRequest();
        var resolved = new ResolvedRecipient("user@example.com", NotificationChannel.Email, null, null, null);

        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>([resolved]));

        _router.Route.Returns(NotificationChannel.Email);

        var mockChannel = new NotificationChannelMock();
        mockChannel.Channel.Returns(NotificationChannel.Email);
        mockChannel.DeliverAsync.Returns(Task.FromException<DeliveryResult>(new InvalidOperationException("SMTP down")));

        _channelFactory.GetChannel.When(NotificationChannel.Email, Arg.Any<string?>())
.Returns(mockChannel);

        var result = await _pipeline.ExecuteAsync(request);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("SMTP down"));
    }
}
