using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Local.Tests.Unit;

public sealed class RequestPasswordResetTests
{
    private readonly LocalIdentityStoreMock _store = new LocalIdentityStoreMock();
    private readonly SecurityTokenServiceMock _tokenService = new SecurityTokenServiceMock();
    private readonly PasswordResetNotifierMock _notifier = new PasswordResetNotifierMock();
    private readonly ClockMock _clock = new ClockMock();

    private readonly LocalIdentityOptions _optionsValue = new() { ResetTokenExpiry = TimeSpan.FromHours(2) };
    private readonly IOptions<LocalIdentityOptions> _options;

    private readonly DateTimeOffset _now = new(2026, 3, 17, 10, 0, 0, TimeSpan.Zero);

    public RequestPasswordResetTests()
    {
        _options = Options.Create(_optionsValue);
        _clock.UtcNow.Returns(_now);
    }

    [Fact]
    public async Task Execute_WithActiveIdentity_Succeeds()
    {
        var identity = CreateActiveIdentity();
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.GenerateToken.Returns("plaintext-token");
        _tokenService.HashToken.When("plaintext-token").Returns("hashed-token");

        var action = CreateAction("test@example.com");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithActiveIdentity_DeliversPlaintextTokenViaNotifierOnly()
    {
        var identity = CreateActiveIdentity();
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.GenerateToken.Returns("plaintext-token");
        _tokenService.HashToken.When("plaintext-token").Returns("hashed-token");

        var action = CreateAction("test@example.com");
        await action.Execute();

        // The plaintext token is delivered out-of-band — never returned in the result/body.
        _notifier.NotifyAsync.Received(1, "test@example.com", "plaintext-token", _now.Add(_optionsValue.ResetTokenExpiry), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithActiveIdentity_StoresHashedTokenNotPlaintext()
    {
        var identity = CreateActiveIdentity();
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.GenerateToken.Returns("plaintext-token");
        _tokenService.HashToken.When("plaintext-token").Returns("hashed-token");

        var action = CreateAction("test@example.com");
        await action.Execute();

        identity.ResetToken.Should().Be("hashed-token");
        identity.ResetToken.Should().NotBe("plaintext-token");
    }

    [Fact]
    public async Task Execute_WithActiveIdentity_SetsExpiryFromOptions()
    {
        var identity = CreateActiveIdentity();
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.GenerateToken.Returns("plaintext-token");
        _tokenService.HashToken.When("plaintext-token").Returns("hashed-token");

        var action = CreateAction("test@example.com");
        await action.Execute();

        identity.ResetTokenExpiresAt.Should().Be(_now.Add(_optionsValue.ResetTokenExpiry));
    }

    [Fact]
    public async Task Execute_WithActiveIdentity_PersistsUpdate()
    {
        var identity = CreateActiveIdentity();
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));
        _tokenService.GenerateToken.Returns("plaintext-token");
        _tokenService.HashToken.When("plaintext-token").Returns("hashed-token");

        var action = CreateAction("test@example.com");
        await action.Execute();

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
        var identity = CreateActiveIdentity();
        identity.IsActive = false;
        _store.FindByEmailAsync.When("test@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<LocalIdentity?>(identity));

        var action = CreateAction("test@example.com");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        // Equalizing throwaway token work runs, but no persist and no notification (no leak).
        _tokenService.GenerateToken.Received(1);
        _store.UpdateAsync.DidNotReceive();
        _notifier.NotifyAsync.DidNotReceive();
    }

    private RequestPasswordReset CreateAction(string email)
    {
        var action = new RequestPasswordReset { Email = email };
        action.SetDependencies(_store, _tokenService, _notifier, _options, _clock);
        return action;
    }

    private static LocalIdentity CreateActiveIdentity() => new()
    {
        Email = "test@example.com",
        ExternalIdentityKey = "local|test@example.com",
        PasswordHash = "old-hash",
        IsActive = true
    };
}
