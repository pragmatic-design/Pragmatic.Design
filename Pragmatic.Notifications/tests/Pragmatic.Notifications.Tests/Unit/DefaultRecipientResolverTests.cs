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
        _resolver = new DefaultRecipientResolver(_prefs, []);
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

    /// <summary>With no directory a user resolves to no address, and is named as unreached rather than dropped (#743).</summary>
    [Fact]
    public async Task ResolveAsync_WithUserIdAndNoDirectory_NamesTheUserAsUnreached()
    {
        var recipient = NotificationRecipient.User("user-42");

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.EndUser);

        result.Should().BeEmpty();
        result.Unreached.Should().Equal("user 'user-42'");
    }

    [Fact]
    public async Task ResolveAsync_WithRoleAndNoDirectory_NamesTheRoleAsUnreached()
    {
        var recipient = NotificationRecipient.Role("admins");

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.Admin);

        result.Should().BeEmpty();
        result.Unreached.Should().Equal("role 'admins'");
    }

    [Fact]
    public async Task ResolveAsync_WithTenantOnly_NamesTheTenantAsUnreached()
    {
        var recipient = NotificationRecipient.Tenant("tenant-1");

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.Admin);

        result.Should().BeEmpty();
        result.Unreached.Should().Equal("tenant 'tenant-1'");
    }

    /// <summary>A user a directory knows is delivered to their e-mail and phone (#743).</summary>
    [Fact]
    public async Task ResolveAsync_WithUserIdADirectoryKnows_ResolvesToTheirAddresses()
    {
        var resolver = new DefaultRecipientResolver(_prefs, [new Directory()]);

        var result = await resolver.ResolveAsync(NotificationRecipient.User("ada"), NotificationAudience.EndUser);

        result.Select(r => (r.Address, r.Channel)).Should().Equal(("ada@example.com", NotificationChannel.Email), ("+3900", NotificationChannel.Sms));
        result.Unreached.Should().BeEmpty();
    }

    /// <summary>A role reaches each member once, a member who is also named directly included; a member with no address is named.</summary>
    [Fact]
    public async Task ResolveAsync_WithRole_ReachesEachMemberOnce_AndNamesTheOneWithNoAddress()
    {
        var resolver = new DefaultRecipientResolver(_prefs, [new Directory(), new Directory()]);
        var recipient = new NotificationRecipient { UserId = "bob", RoleName = "ops" };

        var result = await resolver.ResolveAsync(recipient, NotificationAudience.Admin);

        result.Select(r => r.Address).Should().Equal("bob@example.com", "ada@example.com", "+3900");
        result.Unreached.Should().Equal("user 'eve' (role 'ops')");
    }

    /// <summary>Knows ada (e-mail and phone) and bob (e-mail); the role ops is held by ada, bob and eve, whose address it does not know.</summary>
    private sealed class Directory : IRecipientDirectory
    {
        public Task<RecipientContact?> FindUserAsync(string userId, CancellationToken ct = default) => Task.FromResult(userId switch
        {
            "ada" => new RecipientContact("ada@example.com", "+3900"),
            "bob" => new RecipientContact("bob@example.com", null),
            _ => (RecipientContact?)null,
        });

        public async IAsyncEnumerable<string> UsersInRoleAsync(string role, string? tenantId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            if (role != "ops")
                yield break;
            yield return "ada";
            yield return "bob";
            yield return "eve";
        }
    }

    [Fact]
    public async Task ResolveAsync_WithEmptyRecipient_ResolvesToNothing()
    {
        var recipient = new NotificationRecipient();

        var result = await _resolver.ResolveAsync(recipient, NotificationAudience.EndUser);

        result.Should().BeEmpty();
    }
}
