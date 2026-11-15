using Pragmatic.Identity.Local.Services;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Identity.Local.Tests.Unit;

/// <summary>
///     A local account answers a notification addressed to its user with the e-mail it signs in with (#743): the identity whose external key
///     is the user's id, as the account's <c>sub</c> carries it.
/// </summary>
public sealed class ALocalAccountIsANotificationsRecipientTests
{
    private readonly LocalIdentityStoreMock _store = new();

    [Fact]
    public async Task A_user_with_a_local_identity_is_answered_with_its_email()
    {
        _store.FindByExternalKeyAsync.When("local|ada@example.com", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<LocalIdentity?>(new LocalIdentity { Email = "ada@example.com", ExternalIdentityKey = "local|ada@example.com" }));

        var contact = await new LocalIdentityRecipientDirectory(_store).FindUserAsync("local|ada@example.com");

        contact.Should().Be(new Pragmatic.Notifications.RecipientContact("ada@example.com", null));
    }

    /// <summary>The control: a user it does not hold is left to another directory, not answered with nothing.</summary>
    [Fact]
    public async Task A_user_it_does_not_hold_is_not_answered()
    {
        _store.FindByExternalKeyAsync.Returns(new ValueTask<LocalIdentity?>((LocalIdentity?)null));

        (await new LocalIdentityRecipientDirectory(_store).FindUserAsync("someone-else")).Should().BeNull();
    }
}
