using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Notifications.Tests.Unit;

/// <summary>
///     A notification addressed to a user or a role, sent through the service as an application registers it (#743): the user is reached
///     at the address an <see cref="IRecipientDirectory" /> knows, a role at each member's once, and a recipient no directory answers is
///     named in the result instead of being dropped with a log line.
/// </summary>
public sealed class AUserOrARoleIsReachedThroughTheDirectoriesTests
{
    [Fact]
    public async Task A_notification_to_a_user_is_delivered_to_their_email()
    {
        var (notifications, email) = Host();

        var result = await notifications.SendAsync(Request(NotificationRecipient.User("ada"))).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        email.Addresses.Should().Equal("ada@example.com");
        result.Unreached.Should().BeEmpty();
    }

    [Fact]
    public async Task A_role_reaches_each_member_once_and_names_the_one_it_could_not_reach()
    {
        var (notifications, email) = Host();

        var result = await notifications.SendAsync(Request(new NotificationRecipient { UserId = "bob", RoleName = "ops" })).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        email.Addresses.Should().Equal("bob@example.com", "ada@example.com");
        result.Unreached.Should().Equal("user 'eve' (role 'ops')");
    }

    /// <summary>Nobody reachable: the send fails, and says whom it could not reach.</summary>
    [Fact]
    public async Task A_user_no_directory_knows_fails_the_send_naming_them()
    {
        var (notifications, email) = Host();

        var result = await notifications.SendAsync(Request(NotificationRecipient.User("ghost"))).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        email.Addresses.Should().BeEmpty();
        result.Unreached.Should().Equal("user 'ghost'");
        result.Errors.Should().ContainSingle().Which.Should().Contain("user 'ghost'");
    }

    private static (INotificationService Notifications, RecordingChannel Email) Host()
    {
        var email = new RecordingChannel();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticNotifications(n => n.AddChannel(email).UseInMemoryStore());
        services.AddScoped<IRecipientDirectory, Directory>();
        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<INotificationService>(), email);
    }

    private static NotificationRequest Request(NotificationRecipient recipient) => new()
    {
        Audience = NotificationAudience.EndUser,
        Recipient = recipient,
        Content = new NotificationContent { Subject = "Shift", Body = "Your shift starts at 9." },
    };

    /// <summary>Knows ada's and bob's e-mail; the role ops is held by ada, bob and eve, whose address it does not know.</summary>
    private sealed class Directory : IRecipientDirectory
    {
        public Task<RecipientContact?> FindUserAsync(string userId, CancellationToken ct = default) => Task.FromResult(userId switch
        {
            "ada" => new RecipientContact("ada@example.com", null),
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

    private sealed class RecordingChannel : INotificationChannel
    {
        public List<string> Addresses { get; } = [];

        public NotificationChannel Channel => NotificationChannel.Email;

        public Task<DeliveryResult> DeliverAsync(ResolvedRecipient recipient, NotificationContent content, CancellationToken ct = default)
        {
            Addresses.Add(recipient.Address);
            return Task.FromResult(DeliveryResult.Succeeded());
        }
    }
}
