using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Identity.Local.Tests.Unit;

/// <summary>
///     Every entry asks the store for the email in the form <c>RegisterUser</c> stored it.
/// </summary>
/// <remarks>
///     <c>RegisterUser</c> lower-cases the email it stores. An entry that passed it to the store as typed
///     would make sign-in for <c>Ada@Example.COM</c> depend on the store: one that lower-cases on its own
///     works, one that does not answers "invalid credentials" on a case-sensitive database, with the
///     right password.
/// </remarks>
public sealed class AnEmailReachesTheStoreAsItIsStoredTests
{
    private const string Typed = "Ada@Example.COM";
    private const string Stored = "ada@example.com";

    private readonly LocalIdentityStoreMock _store = new();
    private readonly PasswordHasherMock _hasher = new();
    private readonly SecurityTokenServiceMock _tokens = new();
    private readonly ClockMock _clock = new();
    private readonly DomainEventDispatcherMock _events = new();
    private readonly IOptions<LocalIdentityOptions> _options = Options.Create(new LocalIdentityOptions());

    [Fact]
    public async Task SigningIn_LooksTheEmailUpAsStored()
    {
        var action = new LoginUser { Email = Typed, Password = "any-password" };
        action.SetDependencies(_store, _hasher, _options, _clock, _events, NullLogger<LoginUser>.Instance);

        await action.Execute();

        _store.FindByEmailAsync.Received(1, Stored, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestingAReset_LooksTheEmailUpAsStored()
    {
        var action = new RequestPasswordReset { Email = Typed };
        action.SetDependencies(_store, _tokens, new PasswordResetNotifierMock(), _options, _clock);

        await action.Execute();

        _store.FindByEmailAsync.Received(1, Stored, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingAReset_LooksTheEmailUpAsStored()
    {
        var policy = new PasswordPolicyMock();
        policy.Validate.Returns(VoidResult<IError>.Success());
        var action = new ConfirmPasswordReset { Email = Typed, Token = "token", NewPassword = "NewSecurePass!" };
        action.SetDependencies(_store, _hasher, _tokens, policy, _events, _clock);

        await action.Execute();

        _store.FindByEmailAsync.Received(1, Stored, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingTheEmail_LooksTheEmailUpAsStored()
    {
        var action = new ConfirmEmail { Email = Typed, Token = "token" };
        action.SetDependencies(_store, _tokens, _clock);

        await action.Execute();

        _store.FindByEmailAsync.Received(1, Stored, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestingVerification_LooksTheEmailUpAsStored()
    {
        var action = new RequestEmailVerification { Email = Typed };
        action.SetDependencies(_store, _tokens, new EmailVerificationNotifierMock(), _options, _clock);

        await action.Execute();

        _store.FindByEmailAsync.Received(1, Stored, Arg.Any<CancellationToken>());
    }

    /// <summary>The form they are looked up in is the one registration writes.</summary>
    [Fact]
    public void TheStoredForm_IsWhatRegistrationWrites()
    {
        LocalIdentity.NormalizeEmail(Typed).Should().Be(Stored);
    }
}
