using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Tests.Unit;

public sealed class RequestEmailVerificationTests
{
    private readonly LocalIdentityStoreMock _store = new LocalIdentityStoreMock();
    private readonly SecurityTokenServiceMock _tokenService = new SecurityTokenServiceMock();
    private readonly EmailVerificationNotifierMock _notifier = new EmailVerificationNotifierMock();
    private readonly ClockMock _clock = new ClockMock();

    private readonly LocalIdentityOptions _optionsValue = new() { EmailVerificationTokenExpiry = TimeSpan.FromHours(6) };
    private readonly IOptions<LocalIdentityOptions> _options;

    private readonly DateTimeOffset _now = new(2026, 3, 17, 10, 0, 0, TimeSpan.Zero);

    public RequestEmailVerificationTests()
    {
        _options = Options.Create(_optionsValue);
        _clock.UtcNow.Returns(_now);
    }

    [Fact]
    public async Task Execute_WithActiveUnverifiedIdentity_Succeeds()
    {
        var identity = CreateUnverifiedIdentity();
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.GenerateToken.Returns("plaintext-token");
        _tokenService.HashToken.When("plaintext-token").Returns("hashed-token");

        var action = CreateAction("test@example.com");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithActiveUnverifiedIdentity_DeliversPlaintextTokenViaNotifierOnly()
    {
        var identity = CreateUnverifiedIdentity();
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.GenerateToken.Returns("plaintext-token");
        _tokenService.HashToken.When("plaintext-token").Returns("hashed-token");

        var action = CreateAction("test@example.com");
        await action.Execute();

        // The plaintext token is delivered out-of-band — never returned in the result/body.
        _notifier.NotifyAsync.Received(1, "test@example.com", "plaintext-token", _now.Add(_optionsValue.EmailVerificationTokenExpiry), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithActiveUnverifiedIdentity_StoresHashedTokenAndExpiry()
    {
        var identity = CreateUnverifiedIdentity();
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.GenerateToken.Returns("plaintext-token");
        _tokenService.HashToken.When("plaintext-token").Returns("hashed-token");

        var action = CreateAction("test@example.com");
        await action.Execute();

        identity.EmailVerificationToken.Should().Be("hashed-token");
        identity.EmailVerificationToken.Should().NotBe("plaintext-token");
        identity.EmailVerificationTokenExpiresAt.Should().Be(_now.Add(_optionsValue.EmailVerificationTokenExpiry));
        _store.UpdateAsync.Received(1, identity, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithUnknownEmail_SucceedsWithoutLeaking()
    {
        _store.FindByEmailAsync.When("missing@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>((LocalIdentity?)null));

        var action = CreateAction("missing@example.com");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownEmail_RunsEqualizingTokenWorkButDoesNotPersistOrNotify()
    {
        _store.FindByEmailAsync.When("missing@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>((LocalIdentity?)null));

        var action = CreateAction("missing@example.com");
        await action.Execute();

        // Throwaway token work runs so the miss path is not trivially faster than the found path...
        _tokenService.GenerateToken.Received(1);
        // ...but nothing is persisted and no notification is sent (no side effect / no existence leak).
        _store.UpdateAsync.DidNotReceive();
        _notifier.NotifyAsync.DidNotReceive();
    }

    [Fact]
    public async Task Execute_WithInactiveIdentity_SucceedsWithoutLeaking()
    {
        var identity = CreateUnverifiedIdentity();
        identity.IsActive = false;
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var action = CreateAction("test@example.com");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        _tokenService.GenerateToken.Received(1);
        _store.UpdateAsync.DidNotReceive();
        _notifier.NotifyAsync.DidNotReceive();
    }

    [Fact]
    public async Task Execute_WithAlreadyVerifiedIdentity_SucceedsWithoutIssuingToken()
    {
        // An already-verified account must be indistinguishable from an unverified one: still uniform
        // success, no new token persisted, no notification — but equalizing work runs.
        var identity = CreateUnverifiedIdentity();
        identity.EmailVerified = true;
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var action = CreateAction("test@example.com");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        _tokenService.GenerateToken.Received(1);
        _store.UpdateAsync.DidNotReceive();
        _notifier.NotifyAsync.DidNotReceive();
    }

    private RequestEmailVerification CreateAction(string email)
    {
        var action = new RequestEmailVerification { Email = email };
        action.SetDependencies(_store, _tokenService, _notifier, _options, _clock);
        return action;
    }

    private static LocalIdentity CreateUnverifiedIdentity() => new()
    {
        Email = "test@example.com",
        ExternalIdentityKey = "local|test@example.com",
        PasswordHash = "old-hash",
        IsActive = true,
        EmailVerified = false
    };
}
