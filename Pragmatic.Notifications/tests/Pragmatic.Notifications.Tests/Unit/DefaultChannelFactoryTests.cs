using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Notifications.Channels;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class DefaultChannelFactoryTests
{
    private static NotificationChannelMock FakeChannel(NotificationChannel channel)
    {
        var fake = new NotificationChannelMock();
        fake.Channel.Returns(channel);
        return fake;
    }

    [Fact]
    public void GetChannel_WhenRegistered_ReturnsChannel()
    {
        var email = FakeChannel(NotificationChannel.Email);
        var factory = new DefaultChannelFactory([email]);

        var result = factory.GetChannel(NotificationChannel.Email);

        result.Should().BeSameAs(email);
    }

    [Fact]
    public void GetChannel_WhenNotRegistered_ReturnsNull()
    {
        var factory = new DefaultChannelFactory([FakeChannel(NotificationChannel.Email)]);

        var result = factory.GetChannel(NotificationChannel.Webhook);

        result.Should().BeNull();
    }

    [Fact]
    public void GetChannel_IgnoresTenantId_ReturnsSameChannel()
    {
        var email = FakeChannel(NotificationChannel.Email);
        var factory = new DefaultChannelFactory([email]);

        var result = factory.GetChannel(NotificationChannel.Email, "tenant-7");

        result.Should().BeSameAs(email);
    }

    [Fact]
    public void GetRegisteredChannels_ReturnsAllRegisteredKeys()
    {
        var factory = new DefaultChannelFactory([
            FakeChannel(NotificationChannel.Email),
            FakeChannel(NotificationChannel.Webhook),
        ]);

        var result = factory.GetRegisteredChannels();

        result.Should().BeEquivalentTo([NotificationChannel.Email, NotificationChannel.Webhook]);
    }

    [Fact]
    public void GetRegisteredChannels_WithNoChannels_ReturnsEmpty()
    {
        var factory = new DefaultChannelFactory([]);

        factory.GetRegisteredChannels().Should().BeEmpty();
    }
}
