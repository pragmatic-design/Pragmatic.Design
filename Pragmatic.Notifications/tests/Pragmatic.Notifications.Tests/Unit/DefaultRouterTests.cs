using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Preferences;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class DefaultRouterTests
{
    private readonly NotificationChannelFactoryMock _factory = new NotificationChannelFactoryMock();
    private readonly DefaultNotificationRouter _router;

    public DefaultRouterTests()
    {
        _factory.GetRegisteredChannels.Returns(new List<NotificationChannel>
        {
            NotificationChannel.Email,
            NotificationChannel.Webhook,
            NotificationChannel.Push,
        });
        _router = new DefaultNotificationRouter(_factory);
    }

    private static ResolvedRecipient CreateRecipient(
        NotificationChannel channel = NotificationChannel.Email,
        NotificationPreferences? prefs = null)
        => new("test@example.com", channel, null, null, prefs);

    [Fact]
    public void Route_CriticalPriority_ReturnsAllChannels()
    {
        var result = _router.Route(CreateRecipient(), NotificationPriority.Critical, null);

        result.HasFlag(NotificationChannel.Email).Should().BeTrue();
        result.HasFlag(NotificationChannel.Webhook).Should().BeTrue();
        result.HasFlag(NotificationChannel.Push).Should().BeTrue();
    }

    [Fact]
    public void Route_HighPriority_ReturnsEmailAndPush()
    {
        var result = _router.Route(CreateRecipient(), NotificationPriority.High, null);

        result.HasFlag(NotificationChannel.Email).Should().BeTrue();
        result.HasFlag(NotificationChannel.Push).Should().BeTrue();
        result.HasFlag(NotificationChannel.Webhook).Should().BeFalse();
    }

    [Fact]
    public void Route_NormalPriority_ReturnsRecipientChannel()
    {
        var result = _router.Route(
            CreateRecipient(NotificationChannel.Webhook),
            NotificationPriority.Normal, null);

        result.Should().Be(NotificationChannel.Webhook);
    }

    [Fact]
    public void Route_LowPriority_ReturnsEmail()
    {
        var result = _router.Route(CreateRecipient(), NotificationPriority.Low, null);

        result.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public void Route_WithDisabledPreferences_ReturnsNone()
    {
        var prefs = new NotificationPreferences { Enabled = false };
        var result = _router.Route(CreateRecipient(prefs: prefs), NotificationPriority.Normal, null);

        result.Should().Be(NotificationChannel.None);
    }

    [Fact]
    public void Route_WithMutedCategory_ReturnsNone()
    {
        var prefs = new NotificationPreferences
        {
            MutedCategories = new HashSet<string> { "marketing" },
        };
        var result = _router.Route(CreateRecipient(prefs: prefs), NotificationPriority.Normal, "marketing");

        result.Should().Be(NotificationChannel.None);
    }

    [Fact]
    public void Route_WithNonMutedCategory_ReturnsChannel()
    {
        var prefs = new NotificationPreferences
        {
            MutedCategories = new HashSet<string> { "marketing" },
        };
        var result = _router.Route(CreateRecipient(prefs: prefs), NotificationPriority.Normal, "transactional");

        result.Should().NotBe(NotificationChannel.None);
    }

    [Fact]
    public void Route_WithNoRegisteredChannels_ReturnsNone()
    {
        var emptyFactory = new NotificationChannelFactoryMock();
        emptyFactory.GetRegisteredChannels.Returns(new List<NotificationChannel>());
        var router = new DefaultNotificationRouter(emptyFactory);

        var result = router.Route(CreateRecipient(), NotificationPriority.Normal, null);

        result.Should().Be(NotificationChannel.None);
    }

    [Theory]
    [InlineData(NotificationPriority.Low)]
    [InlineData(NotificationPriority.Normal)]
    [InlineData(NotificationPriority.High)]
    public void Route_WithDoNotDisturb_HoldsNonCriticalNotifications(NotificationPriority priority)
    {
        var prefs = new NotificationPreferences { DoNotDisturb = true };

        var result = _router.Route(CreateRecipient(prefs: prefs), priority, null);

        result.Should().Be(NotificationChannel.None);
    }

    [Fact]
    public void Route_WithDoNotDisturb_CriticalBypassesAndDelivers()
    {
        var prefs = new NotificationPreferences { DoNotDisturb = true };

        var result = _router.Route(CreateRecipient(prefs: prefs), NotificationPriority.Critical, null);

        result.Should().NotBe(NotificationChannel.None);
    }
}
