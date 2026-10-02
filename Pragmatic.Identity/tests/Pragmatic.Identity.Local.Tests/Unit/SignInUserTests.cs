using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Identity.Local.Tests.Unit;

/// <summary>
///     The package signs a caller in and issues the token: the credentials checked as
///     <see cref="LoginUser" /> checks them, the account's key and security stamp in the token always, and
///     what the application contributes on top.
/// </summary>
public sealed class SignInUserTests
{
    private readonly LocalIdentityStoreMock _store = new LocalIdentityStoreMock();
    private readonly PasswordHasherMock _hasher = new PasswordHasherMock();
    private readonly ClockMock _clock = new ClockMock();
    private readonly IOptions<LocalIdentityOptions> _options = Options.Create(new LocalIdentityOptions());
    private readonly DomainEventDispatcherMock _events = new DomainEventDispatcherMock();
    private readonly AccessTokenIssuerMock _tokens = new AccessTokenIssuerMock();
    private readonly DateTimeOffset _now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);
    private SignInClaims? _signed;

    public SignInUserTests()
    {
        _clock.UtcNow.Returns(_now);
        _tokens.Issue.Returns(claims =>
        {
            _signed = claims;
            return new AccessToken("signed", _now.AddHours(1));
        });
    }

    [Fact]
    public async Task ASignIn_IssuesATokenWithTheAccountsKeyAndSecurityStamp()
    {
        var identity = AnAccount("ada@example.com", password: "right", stamp: "stamp-1");

        var result = await SignIn("ada@example.com", "right").Execute();

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().Be("signed");
        _signed.Should().NotBeNull();
        _signed!.ExternalIdentityKey.Should().Be(identity.ExternalIdentityKey);
        _signed.SecurityStamp.Should().Be("stamp-1", "a token without the stamp is refused on first use");
        _signed.Subject.Should().Be(identity.ExternalIdentityKey, "no contributor set another");
    }

    [Fact]
    public async Task WhatTheApplicationContributes_IsSigned()
    {
        AnAccount("ada@example.com", password: "right", stamp: "stamp-1");
        var contributor = new UserClaimsContributorMock();
        contributor.ContributeAsync.Returns((_, claims, _) =>
        {
            claims.Subject = "ref-42";
            claims.DisplayName = "Ada";
            claims.Roles.Add("employee");
            return ValueTask.CompletedTask;
        });

        await SignIn("ada@example.com", "right", contributor).Execute();

        _signed!.Subject.Should().Be("ref-42");
        _signed.DisplayName.Should().Be("Ada");
        _signed.Roles.Should().Contain("employee");
        _signed.SecurityStamp.Should().Be("stamp-1", "the application adds to the token, it does not replace the stamp");
    }

    /// <summary>The control: a refused sign-in issues no token, and is counted as a login counts it.</summary>
    [Fact]
    public async Task AWrongPassword_IssuesNoToken_AndIsCountedAsAFailedAttempt()
    {
        var identity = AnAccount("ada@example.com", password: "right", stamp: "stamp-1");

        var result = await SignIn("ada@example.com", "wrong").Execute();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidCredentialsError>();
        _signed.Should().BeNull();
        identity.FailedLoginAttempts.Should().Be(1);
    }

    private LocalIdentity AnAccount(string email, string password, string stamp)
    {
        var identity = new LocalIdentity
        {
            Email = email,
            PasswordHash = "hashed-" + password,
            ExternalIdentityKey = $"local|{email}",
            SecurityStamp = stamp,
            IsActive = true
        };
        _store.FindByEmailAsync.When(email, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When(password, identity.PasswordHash).Returns(true);
        return identity;
    }

    private SignInUser SignIn(string email, string password, params IUserClaimsContributor[] contributors)
    {
        var action = new SignInUser { Email = email, Password = password };
        action.SetDependencies(_store, _hasher, _options, _clock, _events, NullLogger<SignInUser>.Instance,
            _tokens, contributors);
        return action;
    }
}
