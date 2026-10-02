using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Events;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Tests.Unit;

public sealed class ConfirmPasswordResetTests
{
    private readonly LocalIdentityStoreMock _store = new LocalIdentityStoreMock();
    private readonly PasswordHasherMock _hasher = new PasswordHasherMock();
    private readonly SecurityTokenServiceMock _tokenService = new SecurityTokenServiceMock();
    private readonly PasswordPolicyMock _passwordPolicy = new PasswordPolicyMock();
    private readonly DomainEventDispatcherMock _events = new DomainEventDispatcherMock();
    private readonly ClockMock _clock = new ClockMock();

    private readonly DateTimeOffset _now = new(2026, 3, 17, 10, 0, 0, TimeSpan.Zero);

    public ConfirmPasswordResetTests()
    {
        _clock.UtcNow.Returns(_now);
        // Default: password policy passes
        _passwordPolicy.Validate.Returns(VoidResult<IError>.Success());
    }

    [Fact]
    public async Task Execute_WithValidToken_ResetsPassword()
    {
        var identity = CreateIdentityWithResetToken();

        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.VerifyToken.When("valid-token", "stored-hash").Returns(true);
        _hasher.Hash.When("NewSecurePass!").Returns("new-hash");

        var action = CreateAction("test@example.com", "valid-token", "NewSecurePass!");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        identity.PasswordHash.Should().Be("new-hash");
        identity.ResetToken.Should().BeNull();
        identity.ResetTokenExpiresAt.Should().BeNull();
        identity.FailedLoginAttempts.Should().Be(0);
        identity.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public async Task Execute_WithValidToken_DispatchesPasswordResetCompleted()
    {
        var identity = CreateIdentityWithResetToken();

        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.VerifyToken.When("valid-token", "stored-hash").Returns(true);
        _hasher.Hash.When("NewSecurePass!").Returns("new-hash");

        var action = CreateAction("test@example.com", "valid-token", "NewSecurePass!");
        await action.Execute();

        _events.DispatchAsyncGeneric.Received<PasswordResetCompleted>(1, __a => __a[0] is PasswordResetCompleted e && (e.ExternalIdentityKey == "local|test@example.com"));
    }

    [Fact]
    public async Task Execute_WithValidButWrongToken_ReturnsInvalidResetToken()
    {
        // Existing, non-expired reset token, but the supplied token does not match.
        var identity = new LocalIdentity
        {
            Email = "test@example.com",
            ExternalIdentityKey = "local|test@example.com",
            PasswordHash = "old-hash",
            ResetToken = "stored-hash",
            ResetTokenExpiresAt = _now.AddHours(1), // not expired
            IsActive = true
        };
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.VerifyToken.When("wrong-token", "stored-hash").Returns(false);

        var action = CreateAction("test@example.com", "wrong-token", "NewSecurePass!");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidResetTokenError>();
        identity.PasswordHash.Should().Be("old-hash");
        identity.ResetToken.Should().Be("stored-hash");
        _store.UpdateAsync.DidNotReceive();
    }

    [Fact]
    public async Task Execute_WithValidToken_RotatesSecurityStamp()
    {
        var identity = CreateIdentityWithResetToken();
        identity.SecurityStamp = "old-stamp";

        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.VerifyToken.When("valid-token", "stored-hash").Returns(true);
        _hasher.Hash.When("NewSecurePass!").Returns("new-hash");

        var action = CreateAction("test@example.com", "valid-token", "NewSecurePass!");
        var result = await action.Execute();

        // Rotating the stamp invalidates JWTs issued before the reset.
        result.IsSuccess.Should().BeTrue();
        identity.SecurityStamp.Should().NotBe("old-stamp");
        identity.SecurityStamp.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_WithExpiredToken_ReturnsInvalidResetToken()
    {
        var identity = new LocalIdentity
        {
            Email = "test@example.com",
            ExternalIdentityKey = "local|test@example.com",
            ResetToken = "stored-hash",
            ResetTokenExpiresAt = _now.AddHours(-1), // Expired
            IsActive = true
        };

        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var action = CreateAction("test@example.com", "any", "any-valid-pass");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidResetTokenError>();
    }

    [Fact]
    public async Task Execute_WithNoResetToken_ReturnsInvalidResetToken()
    {
        var identity = new LocalIdentity
        {
            Email = "test@example.com",
            ExternalIdentityKey = "local|test@example.com",
            ResetToken = null,
            ResetTokenExpiresAt = null,
            IsActive = true
        };

        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var action = CreateAction("test@example.com", "any", "any-valid-pass");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidResetTokenError>();
    }

    [Fact]
    public async Task Execute_WithWeakNewPassword_ReturnsPasswordPolicyError()
    {
        _passwordPolicy.Validate.When("weak")
.Returns(new PasswordPolicyError("Password must be at least 8 characters long."));

        var action = CreateAction("test@example.com", "any-token", "weak");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<PasswordPolicyError>();
    }

    private ConfirmPasswordReset CreateAction(string email, string token, string newPassword)
    {
        var action = new ConfirmPasswordReset
        {
            Email = email,
            Token = token,
            NewPassword = newPassword
        };
        action.SetDependencies(_store, _hasher, _tokenService, _passwordPolicy, _events, _clock);
        return action;
    }

    private LocalIdentity CreateIdentityWithResetToken() => new()
    {
        Email = "test@example.com",
        ExternalIdentityKey = "local|test@example.com",
        PasswordHash = "old-hash",
        ResetToken = "stored-hash",
        ResetTokenExpiresAt = _now.AddHours(1),
        FailedLoginAttempts = 3,
        LockoutEnd = _now.AddMinutes(5),
        IsActive = true
    };
}
