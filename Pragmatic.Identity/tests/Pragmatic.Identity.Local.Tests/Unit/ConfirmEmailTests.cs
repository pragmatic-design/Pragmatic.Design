using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Tests.Unit;

public sealed class ConfirmEmailTests
{
    private readonly LocalIdentityStoreMock _store = new LocalIdentityStoreMock();
    private readonly SecurityTokenServiceMock _tokenService = new SecurityTokenServiceMock();
    private readonly ClockMock _clock = new ClockMock();
    private readonly DateTimeOffset _now = new(2026, 3, 17, 10, 0, 0, TimeSpan.Zero);

    public ConfirmEmailTests() => _clock.UtcNow.Returns(_now);

    [Fact]
    public async Task Execute_WithValidToken_MarksEmailVerifiedAndClearsToken()
    {
        var identity = new LocalIdentity
        {
            Email = "test@example.com",
            ExternalIdentityKey = "local|test@example.com",
            IsActive = true,
            EmailVerified = false,
            EmailVerificationToken = "hashed",
            EmailVerificationTokenExpiresAt = _now.AddHours(1),
        };
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.VerifyToken.When("plain", "hashed").Returns(true);

        var result = await CreateAction("test@example.com", "plain").Execute();

        result.IsSuccess.Should().BeTrue();
        identity.EmailVerified.Should().BeTrue();
        identity.EmailVerificationToken.Should().BeNull();
        identity.EmailVerificationTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Execute_WithExpiredToken_ReturnsInvalid()
    {
        var identity = new LocalIdentity
        {
            Email = "test@example.com",
            ExternalIdentityKey = "local|test@example.com",
            IsActive = true,
            EmailVerificationToken = "hashed",
            EmailVerificationTokenExpiresAt = _now.AddHours(-1), // expired
        };
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.VerifyToken.When("plain", "hashed").Returns(true);

        var result = await CreateAction("test@example.com", "plain").Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidEmailVerificationTokenError>();
        identity.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_WithValidButWrongToken_ReturnsInvalid()
    {
        // Existing, non-expired verification token, but the supplied token does not match.
        var identity = new LocalIdentity
        {
            Email = "test@example.com",
            ExternalIdentityKey = "local|test@example.com",
            IsActive = true,
            EmailVerified = false,
            EmailVerificationToken = "hashed",
            EmailVerificationTokenExpiresAt = _now.AddHours(1), // not expired
        };
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.VerifyToken.When("wrong", "hashed").Returns(false);

        var result = await CreateAction("test@example.com", "wrong").Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidEmailVerificationTokenError>();
        identity.EmailVerified.Should().BeFalse();
        identity.EmailVerificationToken.Should().Be("hashed");
        _store.UpdateAsync.DidNotReceive();
    }

    [Fact]
    public async Task Execute_WithUnknownEmail_ReturnsInvalid()
    {
        _store.FindByEmailAsync.When("missing@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>((LocalIdentity?)null));

        var result = await CreateAction("missing@example.com", "plain").Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidEmailVerificationTokenError>();
    }

    private ConfirmEmail CreateAction(string email, string token)
    {
        var action = new ConfirmEmail { Email = email, Token = token };
        action.SetDependencies(_store, _tokenService, _clock);
        return action;
    }
}
