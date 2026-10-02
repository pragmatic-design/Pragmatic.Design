using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Events;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Tests.Unit;

public sealed class LoginUserTests
{
    private readonly LocalIdentityStoreMock _store = new LocalIdentityStoreMock();
    private readonly PasswordHasherMock _hasher = new PasswordHasherMock();
    private readonly ClockMock _clock = new ClockMock();
    private readonly IOptions<LocalIdentityOptions> _options = Options.Create(new LocalIdentityOptions());
    private readonly DomainEventDispatcherMock _events = new DomainEventDispatcherMock();

    private readonly DateTimeOffset _now = new(2026, 3, 17, 10, 0, 0, TimeSpan.Zero);

    public LoginUserTests()
    {
        _clock.UtcNow.Returns(_now);
    }

    [Fact]
    public async Task Execute_WithValidCredentials_ReturnsLoginResult()
    {
        var identity = CreateIdentity("test@example.com");
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("correct-password", identity.PasswordHash).Returns(true);

        var action = CreateAction("test@example.com", "correct-password");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        result.Value.ExternalIdentityKey.Should().Be("local|test@example.com");
        result.Value.AuthenticatedAt.Should().Be(_now);
    }

    [Fact]
    public async Task Execute_WithValidCredentials_DispatchesUserLoggedIn()
    {
        var identity = CreateIdentity("test@example.com");
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("correct-password", identity.PasswordHash).Returns(true);

        var action = CreateAction("test@example.com", "correct-password");
        await action.Execute();

        _events.DispatchAsyncGeneric.Received<UserLoggedIn>(1, __a => __a[0] is UserLoggedIn e && (e.ExternalIdentityKey == "local|test@example.com" &&
                e.AuthenticatedAt == _now));
    }

    [Fact]
    public async Task Execute_WithWrongPassword_ReturnsInvalidCredentials()
    {
        var identity = CreateIdentity("test@example.com");
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("wrong-password", identity.PasswordHash).Returns(false);

        var action = CreateAction("test@example.com", "wrong-password");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidCredentialsError>();
        identity.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Execute_WithWrongPassword_DispatchesLoginFailed()
    {
        var identity = CreateIdentity("test@example.com");
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("wrong-password", identity.PasswordHash).Returns(false);

        var action = CreateAction("test@example.com", "wrong-password");
        await action.Execute();

        _events.DispatchAsyncGeneric.Received<LoginFailed>(1, __a => __a[0] is LoginFailed e && (e.Email == "test@example.com" && e.Reason == "Invalid password"));
    }

    [Fact]
    public async Task Execute_WithUnknownEmail_ReturnsInvalidCredentials()
    {
        _store.FindByEmailAsync.When("unknown@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>((LocalIdentity?)null));

        var action = CreateAction("unknown@example.com", "any-password");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidCredentialsError>();
    }

    [Fact]
    public async Task Execute_WithUnknownEmail_DispatchesLoginFailed()
    {
        _store.FindByEmailAsync.When("unknown@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>((LocalIdentity?)null));

        var action = CreateAction("unknown@example.com", "any-password");
        await action.Execute();

        _events.DispatchAsyncGeneric.Received<LoginFailed>(1, __a => __a[0] is LoginFailed e && (e.Email == "unknown@example.com" && e.Reason == "Invalid credentials"));
    }

    [Fact]
    public async Task Execute_WithInactiveIdentity_UniformMode_ReturnsInvalidCredentials()
    {
        // Default (uniform): an inactive account must be indistinguishable from a wrong password —
        // return the generic 401, not the distinct 403 IdentityNotActiveError.
        var identity = CreateIdentity("test@example.com");
        identity.IsActive = false;
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var action = CreateAction("test@example.com", "any-password");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidCredentialsError>();
    }

    [Fact]
    public async Task Execute_WithInactiveIdentity_UniformMode_RunsDummyHash()
    {
        // The inactive path must run the same bcrypt work as a live account so it is not a timing oracle.
        var identity = CreateIdentity("test@example.com");
        identity.IsActive = false;
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var action = CreateAction("test@example.com", "any-password");
        await action.Execute();

        _hasher.Hash.Received(1, "any-password");
    }

    [Fact]
    public async Task Execute_WithInactiveIdentity_RevealMode_ReturnsNotActive()
    {
        var identity = CreateIdentity("test@example.com");
        identity.IsActive = false;
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var options = Options.Create(new LocalIdentityOptions { RevealAccountState = true });
        var action = new LoginUser { Email = "test@example.com", Password = "any-password" };
        action.SetDependencies(_store, _hasher, options, _clock, _events, NullLogger<LoginUser>.Instance);

        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<IdentityNotActiveError>();
    }

    [Fact]
    public async Task Execute_WithLockedAccount_UniformMode_ReturnsInvalidCredentials()
    {
        // Default (uniform): a locked account is denied but not revealed via 423 — it looks like any
        // other failure. Lockout awareness comes from the AccountLocked event/email + rate limiter.
        var identity = CreateIdentity("test@example.com");
        identity.LockoutEnd = _now.AddMinutes(10);
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var action = CreateAction("test@example.com", "any-password");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidCredentialsError>();
    }

    [Fact]
    public async Task Execute_WithLockedAccount_UniformMode_RunsDummyHash()
    {
        var identity = CreateIdentity("test@example.com");
        identity.LockoutEnd = _now.AddMinutes(10);
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var action = CreateAction("test@example.com", "any-password");
        await action.Execute();

        _hasher.Hash.Received(1, "any-password");
    }

    [Fact]
    public async Task Execute_WithLockedAccount_UniformMode_StillRefusesAndDoesNotVerify()
    {
        // The lockout is still enforced internally: the password is never even verified while locked.
        var identity = CreateIdentity("test@example.com");
        identity.LockoutEnd = _now.AddMinutes(10);
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var action = CreateAction("test@example.com", "any-password");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        _hasher.Verify.DidNotReceive();
    }

    [Fact]
    public async Task Execute_WithLockedAccount_RevealMode_ReturnsAccountLocked()
    {
        var identity = CreateIdentity("test@example.com");
        identity.LockoutEnd = _now.AddMinutes(10);
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var options = Options.Create(new LocalIdentityOptions { RevealAccountState = true });
        var action = new LoginUser { Email = "test@example.com", Password = "any-password" };
        action.SetDependencies(_store, _hasher, options, _clock, _events, NullLogger<LoginUser>.Instance);

        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<AccountLockedError>();
    }

    [Fact]
    public async Task Execute_WithMaxFailedAttempts_LocksAccount()
    {
        var identity = CreateIdentity("test@example.com");
        identity.FailedLoginAttempts = 4; // One more attempt locks at 5
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("wrong", identity.PasswordHash).Returns(false);

        var action = CreateAction("test@example.com", "wrong");
        await action.Execute();

        identity.LockoutEnd.Should().NotBeNull();
        identity.FailedLoginAttempts.Should().Be(5);
    }

    [Fact]
    public async Task Execute_WithMaxFailedAttempts_DispatchesAccountLocked()
    {
        var identity = CreateIdentity("test@example.com");
        identity.FailedLoginAttempts = 4;
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("wrong", identity.PasswordHash).Returns(false);

        var action = CreateAction("test@example.com", "wrong");
        await action.Execute();

        _events.DispatchAsyncGeneric.Received<AccountLocked>(1, __a => __a[0] is AccountLocked e && (e.ExternalIdentityKey == "local|test@example.com"));
    }

    [Fact]
    public async Task Execute_WithSuccessfulLogin_ResetsFailedAttempts()
    {
        var identity = CreateIdentity("test@example.com");
        identity.FailedLoginAttempts = 3;
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("correct", identity.PasswordHash).Returns(true);

        var action = CreateAction("test@example.com", "correct");
        await action.Execute();

        identity.FailedLoginAttempts.Should().Be(0);
        identity.LockoutEnd.Should().BeNull();
        identity.LastLoginAt.Should().Be(_now);
    }

    [Fact]
    public async Task Execute_WithUnknownEmail_RunsDummyHashToAvoidTimingOracle()
    {
        _store.FindByEmailAsync.When("missing@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>((LocalIdentity?)null));

        var action = CreateAction("missing@example.com", "any-password");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        // Equivalent-cost bcrypt work runs even when the account does not exist (no timing leak).
        _hasher.Hash.Received(1, "any-password");
    }

    [Fact]
    public async Task Execute_ExpiredLockout_WrongPassword_DoesNotImmediatelyReLock()
    {
        var identity = CreateIdentity("test@example.com");
        identity.LockoutEnd = _now.AddMinutes(-1); // expired
        identity.FailedLoginAttempts = 5;           // was at the threshold
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("wrong", identity.PasswordHash).Returns(false);

        var action = CreateAction("test@example.com", "wrong");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        identity.LockoutEnd.Should().BeNull("the expired lockout counter is reset before evaluating the password");
        identity.FailedLoginAttempts.Should().Be(1, "the stale count was reset to 0, then this single failure incremented it");
    }

    [Fact]
    public async Task Execute_RequireEmailVerification_UnverifiedWithCorrectPassword_UniformMode_ReturnsInvalidCredentials()
    {
        // Default (uniform): even with a correct password, an unverified account is denied with the
        // generic 401 so it is not distinguishable from a wrong password.
        var identity = CreateIdentity("test@example.com");
        identity.EmailVerified = false;
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("correct", identity.PasswordHash).Returns(true);

        var options = Options.Create(new LocalIdentityOptions { RequireEmailVerification = true });
        var action = new LoginUser { Email = "test@example.com", Password = "correct" };
        action.SetDependencies(_store, _hasher, options, _clock, _events, NullLogger<LoginUser>.Instance);

        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidCredentialsError>();
    }

    [Fact]
    public async Task Execute_RequireEmailVerification_UnverifiedWithCorrectPassword_RevealMode_ReturnsEmailNotVerified()
    {
        var identity = CreateIdentity("test@example.com");
        identity.EmailVerified = false;
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("correct", identity.PasswordHash).Returns(true);

        var options = Options.Create(new LocalIdentityOptions { RequireEmailVerification = true, RevealAccountState = true });
        var action = new LoginUser { Email = "test@example.com", Password = "correct" };
        action.SetDependencies(_store, _hasher, options, _clock, _events, NullLogger<LoginUser>.Instance);

        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<EmailNotVerifiedError>();
    }

    [Fact]
    public async Task Execute_RequireEmailVerification_Verified_Succeeds()
    {
        var identity = CreateIdentity("test@example.com");
        identity.EmailVerified = true;
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("correct", identity.PasswordHash).Returns(true);

        var options = Options.Create(new LocalIdentityOptions { RequireEmailVerification = true });
        var action = new LoginUser { Email = "test@example.com", Password = "correct" };
        action.SetDependencies(_store, _hasher, options, _clock, _events, NullLogger<LoginUser>.Instance);

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_SuccessfulLogin_WhenHashNeedsRehash_ReHashesAndPersists()
    {
        var identity = CreateIdentity("test@example.com");
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("correct", identity.PasswordHash).Returns(true);
        _hasher.NeedsRehash.When(identity.PasswordHash).Returns(true);
        _hasher.Hash.When("correct").Returns("rehashed-stronger");

        var action = CreateAction("test@example.com", "correct");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        identity.PasswordHash.Should().Be("rehashed-stronger", "an outdated work factor is upgraded on login");
        _store.UpdateAsync.Received(1, identity, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_SuccessfulLogin_WhenNoRehashNeeded_KeepsHash()
    {
        var identity = CreateIdentity("test@example.com");
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("correct", identity.PasswordHash).Returns(true);
        _hasher.NeedsRehash.When(identity.PasswordHash).Returns(false);

        var action = CreateAction("test@example.com", "correct");
        await action.Execute();

        identity.PasswordHash.Should().Be("hashed-password");
        _hasher.Hash.DidNotReceive("correct");
    }

    [Fact]
    public async Task Execute_SuccessfulLogin_WhenRehashThrows_LoginStillSucceeds()
    {
        var identity = CreateIdentity("test@example.com");
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("correct", identity.PasswordHash).Returns(true);
        _hasher.NeedsRehash.When(identity.PasswordHash).Returns(true);
        _hasher.Hash.When("correct").Throws(new InvalidOperationException("hashing boom"));

        var action = CreateAction("test@example.com", "correct");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue("a rehash failure must never block an otherwise-valid login");
        identity.PasswordHash.Should().Be("hashed-password", "the existing valid hash is retained");
    }

    private LoginUser CreateAction(string email, string password)
    {
        var action = new LoginUser { Email = email, Password = password };
        action.SetDependencies(_store, _hasher, _options, _clock, _events, NullLogger<LoginUser>.Instance);
        return action;
    }

    private static LocalIdentity CreateIdentity(string email) => new()
    {
        Email = email,
        PasswordHash = "hashed-password",
        ExternalIdentityKey = $"local|{email}",
        IsActive = true
    };
}
