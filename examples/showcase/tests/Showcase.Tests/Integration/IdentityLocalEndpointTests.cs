using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Actions.Invoker;
using Pragmatic.Events;
using Pragmatic.Identity.Local;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;
using Xunit;

namespace Showcase.Tests.Integration;

/// <summary>
///     Integration tests for the Identity.Local package exposed via [ExposeEndpoint] in Showcase.Accounts.
///     Uses DomainActionInvoker (the real DI pipeline) to validate end-to-end.
/// </summary>
public class IdentityLocalEndpointTests : IDisposable
{
    private readonly InMemoryLocalIdentityStore _store = new();
    private readonly CapturingPasswordResetNotifier _notifier = new();
    private readonly DomainEventDispatcherMock _events;
    private readonly ClockMock _clock;
    private readonly ServiceProvider _sp;

    public IdentityLocalEndpointTests()
    {
        _events = new DomainEventDispatcherMock();
        _events.DispatchAsync.Returns(Task.CompletedTask);
        _clock = new ClockMock();
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        _sp = BuildProvider(new LocalIdentityOptions(), _store);
    }

    /// <summary>
    ///     Wires the Identity.Local pipeline. Takes the options so a test can exercise a different security
    ///     posture, and the store so most tests can share the fixture's one while an isolated test gets a
    ///     fresh one.
    /// </summary>
    private ServiceProvider BuildProvider(LocalIdentityOptions options, ILocalIdentityStore? store = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<IDomainEventDispatcher>(_events);
        services.AddSingleton<ILocalIdentityStore>(store ?? new InMemoryLocalIdentityStore());
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<IPasswordHasher>(new BcryptPasswordHasher(workFactor: 4)); // Low for fast tests
        services.AddSingleton<IPasswordPolicy>(new DefaultPasswordPolicy(Options.Create(options)));
        services.AddSingleton<ISecurityTokenService>(new HmacSecurityTokenService());
        services.AddSingleton<IPasswordResetNotifier>(_notifier);
        services.AddLogging();
        Pragmatic.Actions.Extensions.ServiceCollectionExtensions.AddPragmaticActions(services);

        // The generated registration for Identity.Local, rather than a hand-picked list of its
        // invokers. It registers all seven plus the generated permission registry — and that registry
        // is why a hand-written list does not work: an absent registry is refused instead of read as
        // "nothing required", so a container that wires the invokers and skips the registry is the
        // exact shape the fail-closed exists to catch. Calling the generated method also keeps this
        // test from drifting from what the module actually registers.
        Pragmatic.Identity.Local.Actions.PragmaticActionsRegistrationExtensions.AddPragmaticActions(services);

        return services.BuildServiceProvider();
    }

    public void Dispose() => _sp.Dispose();

    [Fact]
    public async Task Register_NewUser_ReturnsExternalIdentityKey()
    {
        using var scope = _sp.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<RegisterUser, string>>();

        var result = await invoker.InvokeAsync(new RegisterUser { Email = "alice@showcase.dev", Password = "SecurePass123!" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("local|alice%40showcase.dev");
    }

    /// <summary>
    ///     Default posture (<c>RevealAccountState = false</c>): registering an existing e-mail must answer
    ///     exactly like a fresh registration, so the endpoint is not an account-existence oracle. It must
    ///     also not create a second identity or overwrite the first one's password.
    /// </summary>
    /// <remarks>
    ///     Asserting a failure result here would keep a green test describing an endpoint that is an
    ///     enumeration oracle. The security decision is the contract — the test follows it.
    /// </remarks>
    [Fact]
    public async Task Register_DuplicateEmail_WithUniformResponses_DoesNotRevealExistence()
    {
        using var scope = _sp.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<RegisterUser, string>>();
        var loginInvoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<LoginUser, LoginResult>>();

        var first = await invoker.InvokeAsync(new RegisterUser { Email = "bob@showcase.dev", Password = "SecurePass123!" });
        var second = await invoker.InvokeAsync(new RegisterUser { Email = "bob@showcase.dev", Password = "Other456!" });

        second.IsSuccess.Should().BeTrue("a distinguishable failure would confirm the address is taken");
        second.Value.Should().Be(first.Value, "the response must be indistinguishable from a fresh registration");

        // The second call must not have replaced the stored credentials.
        var login = await loginInvoker.InvokeAsync(new LoginUser { Email = "bob@showcase.dev", Password = "SecurePass123!" });
        login.IsSuccess.Should().BeTrue("the original password must still work");

        var withNewPassword = await loginInvoker.InvokeAsync(new LoginUser { Email = "bob@showcase.dev", Password = "Other456!" });
        withNewPassword.IsSuccess.Should().BeFalse("the duplicate registration must not have overwritten the identity");
    }

    /// <summary>
    ///     Opt-in posture (<c>RevealAccountState = true</c>): the caller has accepted the enumeration
    ///     trade-off, so the duplicate is reported as <c>EmailAlreadyExistsError</c>.
    /// </summary>
    [Fact]
    public async Task Register_DuplicateEmail_WithRevealAccountState_ReturnsEmailAlreadyExists()
    {
        using var sp = BuildProvider(new LocalIdentityOptions { RevealAccountState = true });
        using var scope = sp.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<RegisterUser, string>>();

        await invoker.InvokeAsync(new RegisterUser { Email = "bob@showcase.dev", Password = "SecurePass123!" });
        var result = await invoker.InvokeAsync(new RegisterUser { Email = "bob@showcase.dev", Password = "Other456!" });

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsLoginResult()
    {
        using var scope = _sp.CreateScope();
        var registerInvoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<RegisterUser, string>>();
        var loginInvoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<LoginUser, LoginResult>>();

        await registerInvoker.InvokeAsync(new RegisterUser { Email = "charlie@showcase.dev", Password = "SecurePass123!" });
        var result = await loginInvoker.InvokeAsync(new LoginUser { Email = "charlie@showcase.dev", Password = "SecurePass123!" });

        result.IsSuccess.Should().BeTrue();
        result.Value.ExternalIdentityKey.Should().Be("local|charlie%40showcase.dev");
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsInvalidCredentials()
    {
        using var scope = _sp.CreateScope();
        var registerInvoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<RegisterUser, string>>();
        var loginInvoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<LoginUser, LoginResult>>();

        await registerInvoker.InvokeAsync(new RegisterUser { Email = "dave@showcase.dev", Password = "SecurePass123!" });
        var result = await loginInvoker.InvokeAsync(new LoginUser { Email = "dave@showcase.dev", Password = "WrongPass!" });

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Login_AccountLockout_AfterMaxFailedAttempts()
    {
        using var scope = _sp.CreateScope();
        var registerInvoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<RegisterUser, string>>();
        var loginInvoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<LoginUser, LoginResult>>();

        await registerInvoker.InvokeAsync(new RegisterUser { Email = "eve@showcase.dev", Password = "SecurePass123!" });

        // Fail 5 times (default MaxFailedLoginAttempts)
        for (var i = 0; i < 5; i++)
            await loginInvoker.InvokeAsync(new LoginUser { Email = "eve@showcase.dev", Password = "Wrong!" });

        // 6th attempt — account locked even with correct password
        var result = await loginInvoker.InvokeAsync(new LoginUser { Email = "eve@showcase.dev", Password = "SecurePass123!" });

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task PasswordReset_FullFlow_RegisterResetLogin()
    {
        using var scope = _sp.CreateScope();
        var registerInvoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<RegisterUser, string>>();
        var resetInvoker = scope.ServiceProvider.GetRequiredService<IVoidDomainActionInvoker<RequestPasswordReset>>();
        var confirmInvoker = scope.ServiceProvider.GetRequiredService<IVoidDomainActionInvoker<ConfirmPasswordReset>>();
        var loginInvoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<LoginUser, LoginResult>>();

        // Register
        await registerInvoker.InvokeAsync(new RegisterUser { Email = "frank@showcase.dev", Password = "SecurePass123!" });

        // Request reset — the plaintext token is delivered out-of-band via the notifier, never returned.
        var resetResult = await resetInvoker.InvokeAsync(new RequestPasswordReset { Email = "frank@showcase.dev" });
        resetResult.IsSuccess.Should().BeTrue();
        _notifier.LastToken.Should().NotBeNullOrEmpty();

        // Confirm reset using the token delivered to the notifier
        var confirmResult = await confirmInvoker.InvokeAsync(new ConfirmPasswordReset
        {
            Email = "frank@showcase.dev",
            Token = _notifier.LastToken!,
            NewPassword = "ResetPass789!"
        });
        confirmResult.IsSuccess.Should().BeTrue();

        // Login with new password
        var loginResult = await loginInvoker.InvokeAsync(new LoginUser { Email = "frank@showcase.dev", Password = "ResetPass789!" });
        loginResult.IsSuccess.Should().BeTrue();
    }

    /// <summary>Simple in-memory store for testing (avoids EF Core dependency).</summary>
    private sealed class InMemoryLocalIdentityStore : ILocalIdentityStore
    {
        private readonly List<LocalIdentity> _identities = [];

        public ValueTask<LocalIdentity?> FindByEmailAsync(string email, CancellationToken ct = default) =>
            new(_identities.FirstOrDefault(i => i.Email == email));

        public ValueTask<LocalIdentity?> FindByExternalKeyAsync(string externalKey, CancellationToken ct = default) =>
            new(_identities.FirstOrDefault(i => i.ExternalIdentityKey == externalKey));

        public ValueTask<LocalIdentity> CreateAsync(LocalIdentity identity, CancellationToken ct = default)
        {
            _identities.Add(identity);
            return new(identity);
        }

        public ValueTask UpdateAsync(LocalIdentity identity, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public ValueTask<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
            new(_identities.Any(i => i.Email == email));
    }

    /// <summary>Captures the out-of-band reset token so the test can complete the confirm step.</summary>
    private sealed class CapturingPasswordResetNotifier : IPasswordResetNotifier
    {
        public string? LastEmail { get; private set; }
        public string? LastToken { get; private set; }

        public Task NotifyAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
        {
            LastEmail = email;
            LastToken = token;
            return Task.CompletedTask;
        }
    }
}
