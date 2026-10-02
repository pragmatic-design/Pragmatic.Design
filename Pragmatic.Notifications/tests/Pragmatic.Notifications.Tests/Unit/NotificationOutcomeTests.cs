using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Preferences;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.Tests.Unit;

/// <summary>
///     The outcome a caller sees must match what actually happened, and must agree with the tracking
///     record written for the same operation.
/// </summary>
public sealed class NotificationOutcomeTests
{
    private readonly RecipientResolverMock _resolver = new RecipientResolverMock();
    private readonly NotificationRouterMock _router = new NotificationRouterMock();
    private readonly NotificationChannelFactoryMock _channelFactory = new NotificationChannelFactoryMock();
    private readonly INotificationStore _store = new InMemoryNotificationStore();
    private readonly NotificationPipeline _pipeline;

    public NotificationOutcomeTests()
        => _pipeline = new NotificationPipeline(
            _resolver, _router, _channelFactory, _store, NullLogger<NotificationPipeline>.Instance);

    private static NotificationRequest CreateRequest(
        NotificationChannel? channelOverride = null,
        string? category = null,
        NotificationPriority priority = NotificationPriority.Normal) => new()
    {
        Audience = NotificationAudience.EndUser,
        Recipient = NotificationRecipient.Direct("user@example.com"),
        Content = new NotificationContent { Subject = "Test", Body = "Body" },
        ChannelOverride = channelOverride,
        Category = category,
        Priority = priority,
    };

    private void ResolvesTo(NotificationPreferences? preferences)
    {
        var resolved = new ResolvedRecipient("user@example.com", NotificationChannel.Email, null, null, preferences);
        _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>([resolved]));
    }

    private NotificationChannelMock RegisterEmailChannel(bool succeeds = true)
    {
        var channel = new NotificationChannelMock();
        channel.Channel.Returns(NotificationChannel.Email);
        channel.DeliverAsync.Returns(Task.FromResult(succeeds ? DeliveryResult.Succeeded("id") : DeliveryResult.Failed("boom")));

        _channelFactory.GetChannel.When(NotificationChannel.Email, Arg.Any<string?>()).Returns(channel);
        _router.Route.Returns(NotificationChannel.Email);

        return channel;
    }

    [Fact]
    public async Task Execute_WithNoChannelRegistered_Fails()
    {
        // Success with zero deliveries would let an app that forgot to register a channel see every
        // notification "succeed" while nothing was ever sent.
        ResolvesTo(null);
        _router.Route.Returns(NotificationChannel.Email);
        _channelFactory.GetChannel.Returns((INotificationChannel?)null);

        var result = await _pipeline.ExecuteAsync(CreateRequest());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("no delivery channel is registered", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Execute_WhenPreferencesSuppressEverything_FailsAndSaysSo()
    {
        ResolvesTo(new NotificationPreferences { Enabled = false });
        RegisterEmailChannel();

        var result = await _pipeline.ExecuteAsync(CreateRequest());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("preferences", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Execute_WithNothingDelivered_TrackingRecordAgreesWithResult()
    {
        // The result and the tracking record must not contradict each other for the same operation,
        // e.g. Success=true to the caller and Failed in the store.
        ResolvesTo(new NotificationPreferences { Enabled = false });
        RegisterEmailChannel();

        var tracking = await _store.CreateAsync(new NotificationRecord
        {
            Audience = NotificationAudience.EndUser,
            RecipientAddress = "user@example.com",
            Channel = NotificationChannel.None,
            Subject = "Test",
            Status = DeliveryStatus.Pending,
        });

        var result = await _pipeline.ExecuteAsync(CreateRequest(), tracking.Id);

        result.Success.Should().BeFalse();
        var stored = await _store.GetByIdAsync(tracking.Id);
        stored!.Status.Should().Be(DeliveryStatus.Failed);
    }

    [Fact]
    public async Task Execute_WithChannelOverride_StillHonoursDoNotDisturb()
    {
        // Regression: ChannelOverride skipped the router entirely, and with it every preference check —
        // an opt-out or do-not-disturb was silently ignored whenever the caller pinned a channel.
        ResolvesTo(new NotificationPreferences { DoNotDisturb = true });
        var channel = RegisterEmailChannel();

        var result = await _pipeline.ExecuteAsync(CreateRequest(channelOverride: NotificationChannel.Email));

        result.Success.Should().BeFalse();
        channel.DeliverAsync.DidNotReceive();
    }

    [Fact]
    public async Task Execute_WithChannelOverride_StillHonoursMutedCategory()
    {
        ResolvesTo(new NotificationPreferences
        {
            MutedCategories = new HashSet<string>(StringComparer.Ordinal) { "marketing" },
        });
        var channel = RegisterEmailChannel();

        var result = await _pipeline.ExecuteAsync(
            CreateRequest(channelOverride: NotificationChannel.Email, category: "marketing"));

        result.Success.Should().BeFalse();
        channel.DeliverAsync.DidNotReceive();
    }

    [Fact]
    public async Task Execute_WithChannelOverride_CriticalBypassesDoNotDisturb()
    {
        ResolvesTo(new NotificationPreferences { DoNotDisturb = true });
        var channel = RegisterEmailChannel();

        var result = await _pipeline.ExecuteAsync(CreateRequest(
            channelOverride: NotificationChannel.Email, priority: NotificationPriority.Critical));

        result.Success.Should().BeTrue();
        channel.DeliverAsync.Received(1);
    }

    [Fact]
    public async Task Execute_WithSuccessfulDelivery_Succeeds()
    {
        ResolvesTo(null);
        RegisterEmailChannel();

        var result = await _pipeline.ExecuteAsync(CreateRequest());

        result.Success.Should().BeTrue();
        result.DeliveryIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task Execute_WithFailingChannel_Fails()
    {
        ResolvesTo(null);
        RegisterEmailChannel(succeeds: false);

        var result = await _pipeline.ExecuteAsync(CreateRequest());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("boom", StringComparison.Ordinal));
    }
}
