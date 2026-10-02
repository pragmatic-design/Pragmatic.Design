using Pragmatic.Testing.Assertions;
using Pragmatic.Audit;
using Pragmatic.Identity.Local.Events;
using Pragmatic.Privacy;

namespace Pragmatic.Identity.Auditing.Tests;

/// <summary>
///     What a failed login and a lockout leave in the trail — and, above all, what they do not.
/// </summary>
public class SecurityEventAuditingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 18, 0, 0, TimeSpan.Zero);
    private const string KnownEmail = "ada@example.com";

    private sealed class RecordingTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public ValueTask RecordAsync(AuditEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return default;
        }
    }

    /// <summary>Knows one subject. Allocating is a failure, not a fallback — see the resolver.</summary>
    private sealed class OneSubjectRegistry : ISubjectRegistry
    {
        public bool AllocationAttempted { get; private set; }

        public ValueTask<string> GetOrCreateReferenceAsync(string subjectType, string identifier, CancellationToken ct = default)
        {
            AllocationAttempted = true;
            return new("should-never-happen");
        }

        public ValueTask<string?> FindReferenceAsync(string subjectType, string identifier, CancellationToken ct = default)
            => new(identifier == KnownEmail ? "subject-ada" : null);

        public ValueTask<string?> ResolveIdentityAsync(string subjectRef, CancellationToken ct = default) => new((string?)null);

        public ValueTask<bool> ForgetAsync(string subjectRef, CancellationToken ct = default) => new(true);
    }

    private static (RecordingTrail Trail, OneSubjectRegistry Registry) Build()
    {
        var registry = new OneSubjectRegistry();
        return (new RecordingTrail(), registry);
    }

    [Fact]
    public async Task AFailedLoginAgainstAKnownAccount_CarriesItsPseudonym()
    {
        var (trail, registry) = Build();
        var handler = new LoginFailedAuditHandler(trail, new ObservedIdentityResolver(registry), new LoginIdentityIsTheSubjectKey());

        await handler.HandleAsync(new LoginFailed(KnownEmail, "wrong password", Now));

        var entry = trail.Entries.Should().ContainSingle().Subject;
        entry.Operation.Should().Be("Security.LoginFailed");
        entry.Category.Should().Be(AuditCategory.Security);
        entry.Outcome.Should().Be(AuditOutcome.Failed);
        entry.SubjectRef.Should().Be("subject-ada");
        entry.OccurredAt.Should().Be(Now);
    }

    [Fact]
    public async Task AFailedLoginAgainstAnUnknownIdentity_IsRecordedWithNoSubject()
    {
        // Not dropped. A burst against accounts that do not exist is what enumeration looks like, and
        // it is often the more interesting signal precisely because it cannot be attributed.
        var (trail, registry) = Build();
        var handler = new LoginFailedAuditHandler(trail, new ObservedIdentityResolver(registry), new LoginIdentityIsTheSubjectKey());

        await handler.HandleAsync(new LoginFailed("stranger@example.com", "no such user", Now));

        var entry = trail.Entries.Should().ContainSingle().Subject;
        entry.SubjectRef.Should().BeNull();
        registry.AllocationAttempted.Should().BeFalse(
            "pseudonymising a typed-in address would let anyone fill the subject registry");
    }

    [Fact]
    public async Task TheAttemptedAddress_NeverReachesTheTrail()
    {
        // The obligation LoginFailed's own documentation hands to whoever persists it.
        var (trail, registry) = Build();
        var handler = new LoginFailedAuditHandler(trail, new ObservedIdentityResolver(registry), new LoginIdentityIsTheSubjectKey());

        await handler.HandleAsync(new LoginFailed(KnownEmail, "wrong password", Now));

        var entry = trail.Entries[0];
        entry.Detail.Should().NotContain(KnownEmail);
        entry.TargetId.Should().NotBe(KnownEmail);
        entry.ActorRef.Should().NotBe(KnownEmail);
    }

    [Fact]
    public async Task TheReasonIsKept_BecauseItIsWhyTheAttemptFailed()
    {
        var (trail, registry) = Build();
        var handler = new LoginFailedAuditHandler(trail, new ObservedIdentityResolver(registry), new LoginIdentityIsTheSubjectKey());

        await handler.HandleAsync(new LoginFailed(KnownEmail, "wrong password", Now));

        trail.Entries[0].Detail.Should().Be("wrong password");
    }

    [Fact]
    public async Task ALockout_IsRecordedAsDeniedRatherThanFailed()
    {
        // The credentials were not rejected; the account was closed to attempts. A reader filtering for
        // genuine authentication failures should not find lockouts mixed in.
        var (trail, registry) = Build();
        var handler = new AccountLockedAuditHandler(trail, new ObservedIdentityResolver(registry), new LoginIdentityIsTheSubjectKey());

        await handler.HandleAsync(new AccountLocked(KnownEmail, Now.AddMinutes(30), Now));

        var entry = trail.Entries.Should().ContainSingle().Subject;
        entry.Operation.Should().Be("Security.AccountLocked");
        entry.Outcome.Should().Be(AuditOutcome.Denied);
        entry.SubjectRef.Should().Be("subject-ada");
        entry.Detail.Should().Contain("locked until");
    }

    [Fact]
    public async Task AnIndefiniteLockout_SaysSo()
    {
        var (trail, registry) = Build();
        var handler = new AccountLockedAuditHandler(trail, new ObservedIdentityResolver(registry), new LoginIdentityIsTheSubjectKey());

        await handler.HandleAsync(new AccountLocked(KnownEmail, null, Now));

        trail.Entries[0].Detail.Should().Be("locked indefinitely");
    }

    // =========================================================================
    // An application whose registry is keyed by anything else
    // =========================================================================

    private const string EmployeeNumber = "EMP-00042";

    /// <summary>
    ///     A registry keyed the way the framework's own <c>[DataSubject]</c> shape asks for: a type of
    ///     the application's and an identifier that is not the address.
    /// </summary>
    private sealed class EmployeeRegistry : ISubjectRegistry
    {
        public bool AllocationAttempted { get; private set; }

        public ValueTask<string> GetOrCreateReferenceAsync(string subjectType, string identifier, CancellationToken ct = default)
        {
            AllocationAttempted = true;
            return new("should-never-happen");
        }

        public ValueTask<string?> FindReferenceAsync(string subjectType, string identifier, CancellationToken ct = default)
            => new(subjectType == "Employee" && identifier == EmployeeNumber ? "subject-ada" : null);

        public ValueTask<string?> ResolveIdentityAsync(string subjectRef, CancellationToken ct = default) => new((string?)null);

        public ValueTask<bool> ForgetAsync(string subjectRef, CancellationToken ct = default) => new(true);
    }

    /// <summary>What such an application knows: this address belongs to that employee, and no other.</summary>
    private sealed class TheEmployeeWhoseAddressThisIs : ISecuritySubjectLocator
    {
        public ValueTask<SecuritySubjectKey?> LocateAsync(
            string identity, LoginIdentityKind kind, CancellationToken ct = default)
        {
            var address = kind == LoginIdentityKind.ExternalIdentityKey
                ? identity.Split('|') is [_, var subject] ? Uri.UnescapeDataString(subject) : identity
                : identity;

            return new(address == KnownEmail
                ? new SecuritySubjectKey("Employee", EmployeeNumber)
                : null);
        }
    }

    /// <summary>
    ///     With a locator of the application's, a failed sign-in against a known account carries that
    ///     account's pseudonym.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Were the pair a <c>const</c> on the handler — <c>("User", &lt;e-mail&gt;)</c> — this
    ///     registry could never match it, the entry would be written with no subject, and the
    ///     per-subject rule that finds credential stuffing against one account would group everything
    ///     under nothing and raise nothing, ever.
    /// </remarks>
    [Fact]
    public async Task WithTheApplicationsLocator_AFailedLoginCarriesTheSubjectItsRegistryHolds()
    {
        var registry = new EmployeeRegistry();
        var trail = new RecordingTrail();
        var handler = new LoginFailedAuditHandler(
            trail, new ObservedIdentityResolver(registry), new TheEmployeeWhoseAddressThisIs());

        await handler.HandleAsync(new LoginFailed(KnownEmail, "wrong password", Now));

        trail.Entries.Should().ContainSingle().Which.SubjectRef.Should().Be("subject-ada");
    }

    /// <summary>
    ///     The lockout resolves too.
    /// </summary>
    /// <remarks>
    ///     A lockout always concerns an account that exists, so unlike a failed login it must resolve
    ///     to a subject — through the application's locator, not a pair fixed on the handler.
    ///     ⚠️ The identity it carries is the composed <c>{issuer}|{subject}</c> key, not what was typed,
    ///     which is why the locator is told which kind it is given.
    /// </remarks>
    [Fact]
    public async Task WithTheApplicationsLocator_ALockoutCarriesTheSubjectToo()
    {
        var registry = new EmployeeRegistry();
        var trail = new RecordingTrail();
        var handler = new AccountLockedAuditHandler(
            trail, new ObservedIdentityResolver(registry), new TheEmployeeWhoseAddressThisIs());

        await handler.HandleAsync(new AccountLocked(
            ExternalIdentityKey.Compose("local", KnownEmail)!, Now.AddMinutes(30), Now));

        trail.Entries.Should().ContainSingle().Which.SubjectRef.Should().Be("subject-ada");
    }

    /// <summary>
    ///     The control: an attempt against an identity the application does not know is still
    ///     unattributed, and nothing is allocated for it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without this, "attribution works" is satisfied by allocating a subject for every address
    ///     anybody types — which would let a stranger fill the subject registry by guessing, and would
    ///     have the system create personal data about people as a side effect of rejecting them.
    /// </remarks>
    [Fact]
    public async Task AnIdentityTheApplicationDoesNotKnow_IsStillUnattributedAndAllocatesNothing()
    {
        var registry = new EmployeeRegistry();
        var trail = new RecordingTrail();
        var handler = new LoginFailedAuditHandler(
            trail, new ObservedIdentityResolver(registry), new TheEmployeeWhoseAddressThisIs());

        await handler.HandleAsync(new LoginFailed("stranger@example.com", "no such user", Now));

        trail.Entries.Should().ContainSingle().Which.SubjectRef.Should().BeNull();
        registry.AllocationAttempted.Should().BeFalse();
    }

    /// <summary>
    ///     The control on the other side: a locator that answers null leaves the entry unattributed
    ///     rather than dropping it.
    /// </summary>
    [Fact]
    public async Task ALocatorThatKnowsNobody_StillLeavesTheEntryInTheTrail()
    {
        var registry = new EmployeeRegistry();
        var trail = new RecordingTrail();
        var handler = new LoginFailedAuditHandler(
            trail, new ObservedIdentityResolver(registry), new NobodyIsKnown());

        await handler.HandleAsync(new LoginFailed(KnownEmail, "wrong password", Now));

        var entry = trail.Entries.Should().ContainSingle().Subject;
        entry.Operation.Should().Be("Security.LoginFailed");
        entry.SubjectRef.Should().BeNull();
    }

    private sealed class NobodyIsKnown : ISecuritySubjectLocator
    {
        public ValueTask<SecuritySubjectKey?> LocateAsync(
            string identity, LoginIdentityKind kind, CancellationToken ct = default)
            => new((SecuritySubjectKey?)null);
    }
}
