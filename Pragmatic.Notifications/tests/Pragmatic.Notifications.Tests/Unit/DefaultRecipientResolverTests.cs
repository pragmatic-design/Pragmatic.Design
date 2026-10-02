using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Preferences;

namespace Pragmatic.Notifications.Tests.Unit;

public sealed class DefaultRecipientResolverTests
{
    private readonly NotificationPreferenceProviderMock _prefs = new NotificationPreferenceProviderMock();
    private readonly DefaultRecipientResolver _resolver;

    public DefaultRecipientResolverTests()
    {
        _prefs.GetPreferencesAsync.Returns(new NotificationPreferences());
        _resolver = new DefaultRecipientResolver(_prefs, NullLogger<DefaultRecipientResolver>.Instance);
    }

    [Fact]
    public async Task ResolveAsync_WithEmailAddress_ResolvesToEmailChannel()
    {
        var recipient = NotificationRecipient.Direct("user@example.com");

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.EndUser);

        result.Should().ContainSingle();
        result[0].Address.Should().Be("user@example.com");
        result[0].Channel.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public async Task ResolveAsync_WithEmailAddress_AttachesPreferences()
    {
        var prefs = new NotificationPreferences { DoNotDisturb = true };
        _prefs.GetPreferencesAsync.When("user@example.com", Arg.Any<CancellationToken>()).Returns(prefs);
        var recipient = NotificationRecipient.Direct("user@example.com");

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.EndUser);

        result[0].Preferences.Should().BeSameAs(prefs);
    }

    [Fact]
    public async Task ResolveAsync_WithWebhookUrl_ResolvesToWebhookChannel()
    {
        var recipient = NotificationRecipient.ToWebhook("https://hooks.example.com/x");

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.System);

        result.Should().ContainSingle();
        result[0].Address.Should().Be("https://hooks.example.com/x");
        result[0].Channel.Should().Be(NotificationChannel.Webhook);
    }

    [Fact]
    public async Task ResolveAsync_WithPhoneNumber_ResolvesToSmsChannel()
    {
        var recipient = new NotificationRecipient { PhoneNumber = "+15551234567" };

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.EndUser);

        result.Should().ContainSingle();
        result[0].Address.Should().Be("+15551234567");
        result[0].Channel.Should().Be(NotificationChannel.Sms);
    }

    [Fact]
    public async Task ResolveAsync_WithMultipleDirectAddresses_ResolvesEach()
    {
        var recipient = new NotificationRecipient
        {
            EmailAddress = "user@example.com",
            WebhookUrl = "https://hooks.example.com/x",
            PhoneNumber = "+15551234567",
        };

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.EndUser);

        result.Should().HaveCount(3);
        result.Select(r => r.Channel).Should().Contain([
            NotificationChannel.Email, NotificationChannel.Webhook, NotificationChannel.Sms]);
    }

    [Fact]
    public async Task ResolveAsync_WithUserIdOnly_ResolvesToNothing()
    {
        var recipient = NotificationRecipient.User("user-42");

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.EndUser);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_WithRoleOnly_ResolvesToNothing()
    {
        var recipient = NotificationRecipient.Role("admins");

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.Admin);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_WithTenantOnly_ResolvesToNothing()
    {
        var recipient = NotificationRecipient.Tenant("tenant-1");

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.Admin);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_WithEmptyRecipient_ResolvesToNothing()
    {
        var recipient = new NotificationRecipient();

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.EndUser);

        result.Should().BeEmpty();
    }
}
