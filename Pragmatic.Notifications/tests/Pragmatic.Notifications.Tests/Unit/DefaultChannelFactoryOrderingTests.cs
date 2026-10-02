using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Notifications.Channels;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class DefaultChannelFactoryOrderingTests
{
    private static NotificationChannelMock Channel(NotificationChannel kind)
    {
        var channel = new NotificationChannelMock();
        channel.Channel.Returns(kind);
        return channel;
    }

    [Fact]
    public void GetRegisteredChannels_IsOrderedDeterministically()
    {
        // Routing falls back to "the first registered channel". Taking that from a hash-ordered key
        // collection made the fallback vary between runs, so a low-priority notification could land
        // on SMS instead of e-mail depending on hashing.
        var first = new DefaultChannelFactory(
            [Channel(NotificationChannel.Webhook), Channel(NotificationChannel.Email), Channel(NotificationChannel.Sms)]);
        var second = new DefaultChannelFactory(
            [Channel(NotificationChannel.Sms), Channel(NotificationChannel.Webhook), Channel(NotificationChannel.Email)]);

        first.GetRegisteredChannels().Should().Equal(second.GetRegisteredChannels());
        first.GetRegisteredChannels()[0].Should().Be(NotificationChannel.Email,
            "Email has the lowest flag value and must be the stable fallback");
    }

    [Fact]
    public void Constructor_WithTwoProvidersForTheSameChannel_KeepsTheLastAndDoesNotThrow()
    {
        // Throwing "duplicate key" at startup for a replacement channel would be a hostile way
        // to report what is normally an intentional override.
        var original = Channel(NotificationChannel.Email);
        var replacement = Channel(NotificationChannel.Email);

        var act = () => new DefaultChannelFactory([original, replacement]);

        act.Should().NotThrow();
        new DefaultChannelFactory([original, replacement])
            .GetChannel(NotificationChannel.Email).Should().BeSameAs(replacement);
    }

    [Fact]
    public void GetChannel_ForAnUnregisteredChannel_ReturnsNull()
    {
        var factory = new DefaultChannelFactory([Channel(NotificationChannel.Email)]);

        factory.GetChannel(NotificationChannel.Slack).Should().BeNull();
    }
}
