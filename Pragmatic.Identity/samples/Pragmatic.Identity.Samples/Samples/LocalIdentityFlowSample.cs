using System.Reflection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Authorization;
using Pragmatic.Events;
using Pragmatic.Identity.Local;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;
using Pragmatic.Identity;

namespace Pragmatic.Identity.Samples.Samples;

/// <summary>
///     Identity.Local — full runnable account lifecycle:
///     RegisterUser → LoginUser → ChangePassword → password-reset.
///
///     Exercises the REAL <c>[DomainAction]</c> classes (<see cref="RegisterUser"/>,
///     <see cref="LoginUser"/>, <see cref="ChangePassword"/>,
///     <see cref="RequestPasswordReset"/>, <see cref="ConfirmPasswordReset"/>)
///     against the real <see cref="BcryptPasswordHasher"/>,
///     <see cref="HmacSecurityTokenService"/> and <see cref="DefaultPasswordPolicy"/>.
/// </summary>
/// <remarks>
///     Domain actions declare their dependencies as private fields populated by the
///     SG-generated invoker at runtime (the host resolves them from DI). Outside that
///     pipeline this sample sets those fields with a tiny reflection helper — the SAME
///     technique the module's own unit tests use to drive an action in isolation. In a
///     real host you never write this: you inject the action and call it, and the SG wires
///     the fields. The store and clock here are demo-only fakes; everything else is the
///     production service.
/// </remarks>
public static class LocalIdentityFlowSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. Identity.Local — Register → Login → ChangePassword → Reset");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        RunAsync().GetAwaiter().GetResult();

        Console.WriteLine();
    }

    private static async Task RunAsync()
    {
        // Real services. Low BCrypt work factor (4) keeps the sample fast; a host uses the
        // default 12 (LocalIdentityOptions.PasswordWorkFactor).
        var store = new InMemoryLocalIdentityStore();
        var hasher = new BcryptPasswordHasher(workFactor: 4);
        var options = Options.Create(new LocalIdentityOptions { MaxFailedLoginAttempts = 3, MinPasswordLength = 8 });
        var policy = new DefaultPasswordPolicy(options);
        var tokens = new HmacSecurityTokenService();
        IClock clock = new TestClock(DateTimeOffset.UtcNow);
        var events = new PrintingEventDispatcher();

        const string email = "Alice@Example.com";
        var externalKey = ExternalIdentityKey.Compose(LocalIdentity.Provider, email.ToLowerInvariant())!;

        // ----- 5.1 RegisterUser -------------------------------------------------
        Console.WriteLine("  5.1 RegisterUser");

        var weak = await Run(new RegisterUser { Email = email, Password = "short" },
            ("_store", store), ("_hasher", hasher), ("_passwordPolicy", policy), ("_events", events), ("_clock", clock),
            ("_options", options));
        Console.WriteLine($"     register with weak password : {Describe(weak)} (expects failure — policy)");

        var registered = await Run(new RegisterUser { Email = email, Password = "S3cret-Pass!" },
            ("_store", store), ("_hasher", hasher), ("_passwordPolicy", policy), ("_events", events), ("_clock", clock),
            ("_options", options));
        Console.WriteLine($"     register with valid password: {Describe(registered)} (expects success)");
        Console.WriteLine($"     returned external key       : '{registered.Value}' (email normalized lower-case)");

        var duplicate = await Run(new RegisterUser { Email = email, Password = "Another-Pass1" },
            ("_store", store), ("_hasher", hasher), ("_passwordPolicy", policy), ("_events", events), ("_clock", clock),
            ("_options", options));
        // RevealAccountState is false by default: a duplicate answers exactly like a fresh registration,
        // so the response cannot be used to probe which emails have an account.
        Console.WriteLine($"     register duplicate email    : {Describe(duplicate)}, key '{duplicate.Value}' (expects the same answer as a new account)");
        Console.WriteLine($"     events dispatched           : {events.Drain()} (only the first register — the duplicate created nothing)");
        Console.WriteLine();

        // ----- 5.2 LoginUser ----------------------------------------------------
        Console.WriteLine("  5.2 LoginUser");

        var badLogin = await Run(new LoginUser { Email = email, Password = "wrong-password" },
            ("_store", store), ("_hasher", hasher), ("_options", options), ("_clock", clock), ("_events", events));
        Console.WriteLine($"     login with wrong password   : {Describe(badLogin)} (expects failure → LoginFailed)");

        var goodLogin = await Run(new LoginUser { Email = email, Password = "S3cret-Pass!" },
            ("_store", store), ("_hasher", hasher), ("_options", options), ("_clock", clock), ("_events", events));
        Console.WriteLine($"     login with correct password : {Describe(goodLogin)} (expects success → UserLoggedIn)");
        Console.WriteLine($"     events dispatched           : {events.Drain()} (LoginFailed then UserLoggedIn)");
        Console.WriteLine();

        // ----- 5.3 ChangePassword (authenticated) -------------------------------
        Console.WriteLine("  5.3 ChangePassword");
        // ChangePassword resolves the identity via ICurrentUser.Authentication.ExternalIdentityKey.
        var currentUser = new ExternalKeyCurrentUser(externalKey);

        var changeWrongCurrent = await Run(
            new ChangePassword { CurrentPassword = "not-the-current", NewPassword = "Brand-New-Pass1" },
            ("_store", store), ("_hasher", hasher), ("_currentUser", currentUser),
            ("_passwordPolicy", policy), ("_events", events), ("_clock", clock));
        Console.WriteLine($"     change with wrong current   : {Describe(changeWrongCurrent)} (expects failure)");

        var changeOk = await Run(
            new ChangePassword { CurrentPassword = "S3cret-Pass!", NewPassword = "Brand-New-Pass1" },
            ("_store", store), ("_hasher", hasher), ("_currentUser", currentUser),
            ("_passwordPolicy", policy), ("_events", events), ("_clock", clock));
        Console.WriteLine($"     change with correct current : {Describe(changeOk)} (expects success)");

        var loginNew = await Run(new LoginUser { Email = email, Password = "Brand-New-Pass1" },
            ("_store", store), ("_hasher", hasher), ("_options", options), ("_clock", clock), ("_events", events));
        Console.WriteLine($"     login with new password     : {Describe(loginNew)} (expects success)");
        Console.WriteLine($"     events dispatched           : {events.Drain()} (PasswordChanged then UserLoggedIn)");
        Console.WriteLine();

        // ----- 5.4 Password reset ----------------------------------------------
        Console.WriteLine("  5.4 RequestPasswordReset → ConfirmPasswordReset");

        // The plaintext token is delivered out-of-band via IPasswordResetNotifier (never returned in
        // the result/body). A host wires an email-backed notifier; here we capture it to drive the demo.
        var notifier = new CapturingResetNotifier();

        var unknown = await Run(new RequestPasswordReset { Email = "nobody@example.com" },
            ("_store", store), ("_tokenService", tokens), ("_notifier", notifier), ("_options", options), ("_clock", clock));
        Console.WriteLine($"     request for unknown email   : {Describe(unknown)}, no token issued (anti-enumeration)");

        await Run(new RequestPasswordReset { Email = email },
            ("_store", store), ("_tokenService", tokens), ("_notifier", notifier), ("_options", options), ("_clock", clock));
        var plaintextToken = notifier.LastToken!;
        Console.WriteLine($"     request for known email     : issued token (len {plaintextToken.Length}, delivered out-of-band, hashed at rest)");

        var confirmBad = await Run(
            new ConfirmPasswordReset { Email = email, Token = "dGFtcGVyZWQ=", NewPassword = "Reset-Pass-99" },
            ("_store", store), ("_hasher", hasher), ("_tokenService", tokens),
            ("_passwordPolicy", policy), ("_events", events), ("_clock", clock));
        Console.WriteLine($"     confirm with wrong token    : {Describe(confirmBad)} (expects failure)");

        var confirmOk = await Run(
            new ConfirmPasswordReset { Email = email, Token = plaintextToken, NewPassword = "Reset-Pass-99" },
            ("_store", store), ("_hasher", hasher), ("_tokenService", tokens),
            ("_passwordPolicy", policy), ("_events", events), ("_clock", clock));
        Console.WriteLine($"     confirm with valid token    : {Describe(confirmOk)} (expects success)");

        var loginAfterReset = await Run(new LoginUser { Email = email, Password = "Reset-Pass-99" },
            ("_store", store), ("_hasher", hasher), ("_options", options), ("_clock", clock), ("_events", events));
        Console.WriteLine($"     login with reset password   : {Describe(loginAfterReset)} (expects success)");
        Console.WriteLine($"     events dispatched           : {events.Drain()} (PasswordResetCompleted then UserLoggedIn)");
    }

    // ===== Action driver ====================================================

    /// <summary>
    ///     Populates the action's SG-injected private fields, then runs its real <c>Execute</c>.
    ///     The host's generated invoker does this from DI — reproduced here only because the sample
    ///     runs outside a host pipeline (mirrors the module's own unit tests).
    /// </summary>
    private static async Task<Result<TValue, IError>> Run<TValue>(
        IExecutable<TValue> action, params (string Field, object Value)[] dependencies)
    {
        InjectFields(action, dependencies);
        return await action.Execute();
    }

    private static async Task<VoidResult<IError>> Run(
        IVoidExecutable action, params (string Field, object Value)[] dependencies)
    {
        InjectFields(action, dependencies);
        return await action.Execute();
    }

    private static void InjectFields(object action, (string Field, object Value)[] dependencies)
    {
        var type = action.GetType();
        foreach (var (field, value) in dependencies)
        {
            var info = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Field '{field}' not found on {type.Name}.");
            info.SetValue(action, value);
        }
    }

    // ===== Helpers ==========================================================

    private static string Describe<T>(Result<T, IError> result) =>
        result.IsSuccess ? "OK" : $"FAILED ({result.Error.Title})";

    private static string Describe(VoidResult<IError> result) =>
        result.IsSuccess ? "OK" : $"FAILED ({result.Error.Title})";

    // ===== Demo-only infrastructure =========================================

    /// <summary>
    ///     Captures the out-of-band reset token so the sample can drive the confirm step.
    ///     A host registers an email/SMS-backed <see cref="IPasswordResetNotifier"/> instead.
    /// </summary>
    private sealed class CapturingResetNotifier : IPasswordResetNotifier
    {
        public string? LastToken { get; private set; }

        public Task NotifyAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
        {
            LastToken = token;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     In-memory <see cref="ILocalIdentityStore"/> — a host backs this with EF Core.
    ///     Email lookups are case-insensitive: <c>RegisterUser</c> stores the normalized
    ///     (lower-cased) email, while <c>LoginUser</c>/<c>RequestPasswordReset</c> query by the
    ///     email as the caller typed it. A real database column does this via its collation;
    ///     here we use <see cref="StringComparer.OrdinalIgnoreCase"/> to mirror that.
    /// </summary>
    private sealed class InMemoryLocalIdentityStore : ILocalIdentityStore
    {
        private readonly Dictionary<string, LocalIdentity> _byEmail = new(StringComparer.OrdinalIgnoreCase);

        public ValueTask<LocalIdentity?> FindByEmailAsync(string email, CancellationToken ct = default) =>
            new(_byEmail.GetValueOrDefault(email));

        public ValueTask<LocalIdentity?> FindByExternalKeyAsync(string externalKey, CancellationToken ct = default) =>
            new(_byEmail.Values.FirstOrDefault(i => i.ExternalIdentityKey == externalKey));

        public ValueTask<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
            new(_byEmail.ContainsKey(email));

        public ValueTask<LocalIdentity> CreateAsync(LocalIdentity identity, CancellationToken ct = default)
        {
            _byEmail[identity.Email!] = identity;
            return new ValueTask<LocalIdentity>(identity);
        }

        public ValueTask UpdateAsync(LocalIdentity identity, CancellationToken ct = default)
        {
            _byEmail[identity.Email!] = identity;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     Collects the domain events the actions dispatch (instead of routing them). Call
    ///     <see cref="Drain"/> after a scenario to read and clear them — keeps the printed
    ///     narrative deterministic rather than interleaving event output with flow output.
    /// </summary>
    private sealed class PrintingEventDispatcher : IDomainEventDispatcher
    {
        private readonly List<string> _events = [];

        public string Drain()
        {
            var names = _events.Count == 0 ? "(none)" : string.Join(", ", _events);
            _events.Clear();
            return names;
        }

        public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : IDomainEvent
        {
            _events.Add(typeof(TEvent).Name);
            return Task.CompletedTask;
        }

        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
        {
            foreach (var e in events)
                _events.Add(e.GetType().Name);
            return Task.CompletedTask;
        }
    }

    /// <summary>Minimal <see cref="ICurrentUser"/> carrying only an external identity key.</summary>
    private sealed class ExternalKeyCurrentUser(string externalKey) : ICurrentUser
    {
        public string Id => "user:local";
        public string? DisplayName => null;
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims =>
            new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => new ExternalKeyAuthContext(externalKey);
    }

    private sealed class ExternalKeyAuthContext(string externalKey) : IAuthenticationContext
    {
        public string? Scheme => "Local";
        public string? Protocol => null;
        public string? Issuer => null;
        public string? Subject => null;
        public bool IsMfaAuthenticated => false;
        public DateTimeOffset? AuthenticatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset? ExpiresAt => null;
        public string? ExternalIdentityKey => externalKey;
    }
}
