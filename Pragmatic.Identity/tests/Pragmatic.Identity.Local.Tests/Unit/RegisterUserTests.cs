using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
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

public sealed class RegisterUserTests
{
    private readonly LocalIdentityStoreMock _store = new LocalIdentityStoreMock();
    private readonly PasswordHasherMock _hasher = new PasswordHasherMock();
    private readonly PasswordPolicyMock _passwordPolicy = new PasswordPolicyMock();
    private readonly DomainEventDispatcherMock _events = new DomainEventDispatcherMock();
    private readonly ClockMock _clock = new ClockMock();
    private readonly LocalIdentityOptions _optionsValue = new();
    private readonly IOptions<LocalIdentityOptions> _options;

    private readonly DateTimeOffset _now = new(2026, 3, 17, 10, 0, 0, TimeSpan.Zero);

    public RegisterUserTests()
    {
        _options = Options.Create(_optionsValue);
        _clock.UtcNow.Returns(_now);
        // Default: password policy passes
        _passwordPolicy.Validate.Returns(VoidResult<IError>.Success());
    }

    [Fact]
    public async Task Execute_WithNewEmail_CreatesIdentity()
    {
        _store.EmailExistsAsync.When("new@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<bool>(false));
        _hasher.Hash.When("SecurePass123!").Returns("hashed-pass");
        _store.CreateAsync.Returns((identity, _) => new ValueTask<LocalIdentity>(identity));

        var action = CreateAction("new@example.com", "SecurePass123!");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("local|new%40example.com");
        _store.CreateAsync.Received(1, Arg.Is<LocalIdentity>(i =>
                i.Email == "new@example.com" &&
                i.PasswordHash == "hashed-pass" &&
                i.IsActive), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithNewEmail_DispatchesUserRegistered()
    {
        _store.EmailExistsAsync.When("new@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<bool>(false));
        _hasher.Hash.When("SecurePass123!").Returns("hashed-pass");
        _store.CreateAsync.Returns((identity, _) => new ValueTask<LocalIdentity>(identity));

        var action = CreateAction("new@example.com", "SecurePass123!");
        await action.Execute();

        _events.DispatchAsyncGeneric.Received<UserRegistered>(1, __a => __a[0] is UserRegistered e && (e.ExternalIdentityKey == "local|new%40example.com" &&
                e.Email == "new@example.com"));
    }

    [Fact]
    public async Task Execute_WithExistingEmail_UniformMode_ReturnsSuccessShapeWithoutLeaking()
    {
        // Default (uniform): registering an already-taken email must NOT reveal existence — it returns
        // the same success shape (the deterministic external key) as a fresh registration, and it does
        // not create a duplicate or dispatch UserRegistered.
        _store.EmailExistsAsync.When("existing@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<bool>(true));

        var action = CreateAction("existing@example.com", "AnotherPass1!");
        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue("uniform mode hides existence behind a success-shaped response");
        result.Value.Should().Be("local|existing%40example.com");
        _store.CreateAsync.DidNotReceive();
        _events.DispatchAsyncGeneric.DidNotReceive();
    }

    [Fact]
    public async Task Execute_WithExistingEmail_RevealMode_ReturnsEmailAlreadyExists()
    {
        _optionsValue.RevealAccountState = true;
        _store.EmailExistsAsync.When("existing@example.com", Arg.Any<CancellationToken>())
.Returns(new ValueTask<bool>(true));

        var action = CreateAction("existing@example.com", "any");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<EmailAlreadyExistsError>();
    }

    [Fact]
    public async Task Execute_WithWeakPassword_ReturnsPasswordPolicyError()
    {
        _passwordPolicy.Validate.When("short")
.Returns(new PasswordPolicyError("Password must be at least 8 characters long."));

        var action = CreateAction("new@example.com", "short");
        var result = await action.Execute();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<PasswordPolicyError>();
    }

    [Fact]
    public async Task Execute_WithWeakPassword_DoesNotCreateIdentity()
    {
        _passwordPolicy.Validate.When("short")
.Returns(new PasswordPolicyError("Password must be at least 8 characters long."));

        var action = CreateAction("new@example.com", "short");
        await action.Execute();

        _store.CreateAsync.DidNotReceive();
    }

    private RegisterUser CreateAction(string email, string password)
    {
        var action = new RegisterUser { Email = email, Password = password };
        action.SetDependencies(_store, _hasher, _passwordPolicy, _events, _clock, _options);
        return action;
    }
}
