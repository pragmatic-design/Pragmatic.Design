using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.Tests.Unit;

/// <summary>
///     The pipeline with the default router: whatever the priority, an address is handed only to the channels it belongs to (the
///     recipient's resolved <see cref="ResolvedRecipient.Channel" />). A Critical notification fans out through the recipients the
///     resolver returns, one per address, not by sending one address to every registered channel.
/// </summary>
public sealed class RoutingKeepsEachAddressOnItsChannelTests
{
    private readonly RecipientResolverMock _resolver = new RecipientResolverMock();
    private readonly NotificationChannelFactoryMock _channelFactory = new NotificationChannelFactoryMock();
    private readonly INotificationStore _store = new InMemoryNotificationStore();
    private readonly NotificationPipeline _pipeline;

    public RoutingKeepsEachAddressOnItsChannelTests()
    {
        _channelFactory.GetRegisteredChannels.Returns(new List<NotificationChannel>
        {
            NotificationChannel.Email,
            NotificationChannel.Sms,
            NotificationChannel.Webhook,
        });
        foreach (var channel in new[] { NotificationChannel.Email, NotificationChannel.Sms, NotificationChannel.Webhook })
        {
            var mock = new NotificationChannelMock();
            mock.Channel.Returns(channel);
            mock.DeliverAsync.Returns(Task.FromResult(DeliveryResult.Succeeded()));
            _channelFactory.GetChannel.When(channel, Arg.Any<string?>()).Returns(mock);
        }

        _pipeline = new NotificationPipeline(
            _resolver, new DefaultNotificationRouter(_channelFactory), _channelFactory, _store,
            NullLogger<NotificationPipeline>.Instance);
    }

    private void ResolvesTo(params ResolvedRecipient[] recipients)
        => _resolver.ResolveAsync.Returns(Task.FromResult<IReadOnlyList<ResolvedRecipient>>(recipients));

    private static NotificationRequest Request(NotificationPriority priority) => new()
    {
        Audience = NotificationAudience.Admin,
        Recipient = new NotificationRecipient { EmailAddress = "oncall@example.com", PhoneNumber = "+390000000000" },
        Content = new NotificationContent { Subject = "Datastore unreachable", Body = "Failover triggered." },
        Priority = priority,
    };

    private async Task<List<(NotificationChannel Channel, string Address)>> DeliveriesOf(NotificationResult result)
    {
        result.Success.Should().BeTrue();
        var deliveries = new List<(NotificationChannel, string)>();
        foreach (var id in result.DeliveryIds!)
        {
            var record = await _store.GetByIdAsync(id).ConfigureAwait(false);
            deliveries.Add((record!.Channel, record.RecipientAddress));
        }

        return deliveries;
    }

    [Theory]
    [InlineData(NotificationPriority.Critical)]
    [InlineData(NotificationPriority.High)]
    [InlineData(NotificationPriority.Normal)]
    [InlineData(NotificationPriority.Low)]
    public async Task AnEmailAddressAndAPhoneNumber_GetOneEmailAndOneSms_AndTheWebhookNothing(NotificationPriority priority)
    {
        ResolvesTo(
            new ResolvedRecipient("oncall@example.com", NotificationChannel.Email, null, null, null),
            new ResolvedRecipient("+390000000000", NotificationChannel.Sms, null, null, null));

        var deliveries = await DeliveriesOf(await _pipeline.ExecuteAsync(Request(priority)));

        deliveries.Should().BeEquivalentTo(
            [(NotificationChannel.Email, "oncall@example.com"), (NotificationChannel.Sms, "+390000000000")]);
    }

    [Fact]
    public async Task AnAddressTheResolverDeclaresForTwoChannels_GetsBothOnCritical()
    {
        // The resolver says which channels an address reaches: a flags value is how one address serves two of them.
        ResolvesTo(new ResolvedRecipient("https://hooks.example.com/sms-gateway", NotificationChannel.Webhook | NotificationChannel.Sms, null, null, null));

        var deliveries = await DeliveriesOf(await _pipeline.ExecuteAsync(Request(NotificationPriority.Critical)));

        deliveries.Should().BeEquivalentTo(
            [(NotificationChannel.Sms, "https://hooks.example.com/sms-gateway"), (NotificationChannel.Webhook, "https://hooks.example.com/sms-gateway")]);
    }

    [Fact]
    public async Task AnAddressNoRegisteredChannelReaches_IsNotHandedToAnother_AndTheResultSaysWhy()
    {
        ResolvesTo(new ResolvedRecipient("device-token-1", NotificationChannel.Push, null, null, null));

        var result = await _pipeline.ExecuteAsync(Request(NotificationPriority.Normal));

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("no registered channel reaches", StringComparison.OrdinalIgnoreCase));
    }
}
