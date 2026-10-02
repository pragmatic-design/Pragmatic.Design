using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Events;
using Pragmatic.Identity;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Tests.Unit;

public sealed class ChangePasswordTests
{
    private readonly LocalIdentityStoreMock _store = new LocalIdentityStoreMock();
    private readonly PasswordHasherMock _hasher = new PasswordHasherMock();
    private readonly CurrentUserMock _currentUser = new CurrentUserMock();
    private readonly PasswordPolicyMock _passwordPolicy = new PasswordPolicyMock();
    private readonly DomainEventDispatcherMock _events = new DomainEventDispatcherMock();
    private readonly ClockMock _clock = new ClockMock();

    private readonly DateTimeOffset _now = new(2026, 3, 17, 10, 0, 0, TimeSpan.Zero);

    public ChangePasswordTests()
    {
        _clock.UtcNow.Returns(_now);
        // Default: password policy passes
        _passwordPolicy.Validate.Returns(VoidResult<IError>.Success());

        var auth = new AuthenticationContextMock();
        auth.ExternalIdentityKey.Returns("local|test@example.com");
        _currentUser.Authentication.Returns(auth);
    }

    [Fact]
    public async Task Execute_WithValidCurrentPassword_ChangesPassword()
    {
        var identity = CreateIdentity();
        _store.FindByExternalKeyAsync.When("local|test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("current-pass", identity.PasswordHash).Returns(true);
        _hasher.Hash.When("NewSecurePass!").Returns("new-hash");

        var action = CreateAction("current-pass", "NewSecurePass!");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        identity.PasswordHash.Should().Be("new-hash");
    }

    [Fact]
    public async Task Execute_WithValidCurrentPassword_DispatchesPasswordChanged()
    {
        var identity = CreateIdentity();
        _store.FindByExternalKeyAsync.When("local|test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("current-pass", identity.PasswordHash).Returns(true);
        _hasher.Hash.When("NewSecurePass!").Returns("new-hash");

        var action = CreateAction("current-pass", "NewSecurePass!");
        await action.Execute();

        _events.DispatchAsyncGeneric.Received<PasswordChanged>(1, __a => __a[0] is PasswordChanged e && (e.ExternalIdentityKey == "local|test@example.com"));
    }

    [Fact]
    public async Task Execute_WithValidCurrentPassword_RotatesSecurityStamp()
    {
        var identity = CreateIdentity();
        identity.SecurityStamp = "old-stamp";
        _store.FindByExternalKeyAsync.When("local|test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("current-pass", identity.PasswordHash).Returns(true);
        _hasher.Hash.When("NewSecurePass!").Returns("new-hash");

        var action = CreateAction("current-pass", "NewSecurePass!");
        var result = await action.Execute();

        // Rotating the stamp invalidates JWTs issued before the change.
        result.IsSuccess.Should().BeTrue();
        identity.SecurityStamp.Should().NotBe("old-stamp");
        identity.SecurityStamp.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_WithWrongCurrentPassword_ReturnsInvalidCredentials()
    {
        var identity = CreateIdentity();
        _store.FindByExternalKeyAsync.When("local|test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _hasher.Verify.When("wrong-pass", identity.PasswordHash).Returns(false);

        var action = CreateAction("wrong-pass", "NewSecurePass!");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidCredentialsError>();
    }

    [Fact]
    public async Task Execute_WithInactiveIdentity_ReturnsNotActive()
    {
        var identity = CreateIdentity();
        identity.IsActive = false;
        _store.FindByExternalKeyAsync.When("local|test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var action = CreateAction("any", "NewSecurePass!");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<IdentityNotActiveError>();
    }

    [Fact]
    public async Task Execute_WithUnknownIdentity_ReturnsNotActive()
    {
        _store.FindByExternalKeyAsync.When("local|test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>((LocalIdentity?)null));

        var action = CreateAction("any", "NewSecurePass!");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<IdentityNotActiveError>();
    }

    [Fact]
    public async Task Execute_WithWeakNewPassword_ReturnsPasswordPolicyError()
    {
        _passwordPolicy.Validate.When("weak")
.Returns(new PasswordPolicyError("Password must be at least 8 characters long."));

        var action = CreateAction("current-pass", "weak");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<PasswordPolicyError>();
    }

    [Fact]
    public async Task Execute_WithWeakNewPassword_DoesNotUpdateStore()
    {
        _passwordPolicy.Validate.When("weak")
.Returns(new PasswordPolicyError("Password must be at least 8 characters long."));

        var action = CreateAction("current-pass", "weak");
        await action.Execute();

        _store.UpdateAsync.DidNotReceive();
    }

    private ChangePassword CreateAction(string currentPassword, string newPassword)
    {
        var action = new ChangePassword
        {
            CurrentPassword = currentPassword,
            NewPassword = newPassword
        };
        action.SetDependencies(_store, _hasher, _currentUser, _passwordPolicy, _events, _clock);
        return action;
    }

    private static LocalIdentity CreateIdentity() => new()
    {
        Email = "test@example.com",
        ExternalIdentityKey = "local|test@example.com",
        PasswordHash = "hashed-password",
        IsActive = true
    };
}
