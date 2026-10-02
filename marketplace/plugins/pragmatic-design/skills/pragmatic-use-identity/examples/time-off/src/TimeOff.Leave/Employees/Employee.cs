using Pragmatic.Validation.Types;
using TimeOff.Leave.Infrastructure.Internationalization;

namespace TimeOff.Leave.Entities;

/// <summary>
///     A person who works here — and, in Time off, the only kind of user there is: the employee carries
///     the credentials they sign in with.
/// </summary>
/// <remarks>
///     <para>
///         The team is optional. A team needs a manager, the manager is an employee, and with both sides
///         required the first team could never be created; an employee between teams is also a real state.
///     </para>
///     <para>
///         An employee who leaves is soft-deleted (<c>[SoftDelete]</c>): gone from every read, restorable,
///         and erasable only then. Their requests and allowances are declared from here, as theirs: a
///         read that reaches them through the employee hides them with the employee and shows them again
///         when the employee is restored — which is what the calendar should do, and the form PRAG0705
///         asks for when the hiding is meant.
///     </para>
/// </remarks>
[Entity]
[Auditable]
[Audited]
[SoftDelete]
[ConcurrencyAware]
[PragmaticUser(MatchClaim = "sub")]
[DataSubject(nameof(EmployeeNumber))]
[Unique(nameof(WorkEmail))]
[Relation.ManyToOne<Team>.WithNavigation("Team", Required = false, Inverse = "Members")]
[Relation.OneToMany<Team>.WithNavigation("ManagedTeams", Inverse = "Manager", OnDelete = DeleteBehavior.Restrict)]
[Relation.OneToMany<LeaveRequest>.WithNavigation("LeaveRequests", Inverse = "Employee", OnDelete = DeleteBehavior.Restrict)]
[Relation.OneToMany<Allowance>.WithNavigation("Allowances", Inverse = "Employee", OnDelete = DeleteBehavior.Restrict)]
public partial class Employee : IEntity
{
    /// <summary>
    ///     How people refer to an employee — <c>EMP-00001</c> — numbered by a database sequence when the
    ///     row is written, so two registrations at once cannot take the same number.
    /// </summary>
    /// <remarks>
    ///     The identifier the subject registry knows the employee by. Erased to the pseudonym: the row
    ///     stays, joinable to the requests and the allowances, and names nobody.
    /// </remarks>
    [LogicKey]
    [GeneratedValue("EMP-{SEQ:5}")]
    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
    public string EmployeeNumber { get; private set; } = "";

    [Required]
    [MaxLength(200)]
    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Anonymize)]
    public string FullName { get; private set; } = "";

    /// <summary>Also the account's sign-in name: unique, whatever its case.</summary>
    /// <remarks>Erased to the pseudonym, not blanked: the column is unique, and one blank is all it holds.</remarks>
    [Required]
    [Email]
    [MaxLength(320)]
    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Pseudonymize)]
    public string WorkEmail { get; private set; } = "";

    public AccessRole Role { get; private set; }

    public DateOnly HiredOn { get; private set; }

    /// <summary>
    ///     The language the employee chose, as a culture — <c>it-IT</c>. A request that names a language
    ///     is answered in that one; one that names none, in this; with neither, in the default.
    /// </summary>
    /// <remarks>
    ///     A profile property named <c>PreferredCulture</c>: the generator writes the provider that answers
    ///     in it (<c>EmployeeCultureConfigProvider</c>), and the host turns it on with <c>UseUserCulture()</c>.
    /// </remarks>
    [MaxLength(10)]
    [PersonalData(DataCategory.Behavioural)]
    [ProfileProperty]
    public string? PreferredCulture { get; private set; }

    /// <summary>
    ///     Credentials, lockout and the security stamp that revokes issued tokens. Owned: the columns
    ///     live in the employee's row. Null until HR provisions the account.
    /// </summary>
    public LocalIdentity? Identity { get; set; }

    /// <summary>
    ///     Away on <paramref name="day" />: an approved request covers it. A pending one is not an absence
    ///     yet.
    /// </summary>
    /// <remarks>
    ///     A method, so the day comes from outside: the query passes today from the application's clock
    ///     (<c>[FromClock]</c>), the one the decisions and the withdrawal read. A property would have to
    ///     write <c>DateTime.UtcNow</c>, which the database evaluates with its own clock.
    /// </remarks>
    [ComputedFilter]
    public bool IsAwayOn(DateOnly day)
        => LeaveRequests.Any(LeaveRequestSpecifications.Approved & LeaveRequestSpecifications.Covering(day));

    /// <summary>Has requests waiting for a decision, whenever they are for.</summary>
    [ComputedFilter]
    public bool HasPendingRequests => LeaveRequests.Any(LeaveRequestSpecifications.Pending);

    /// <summary>
    ///     Gives the employee the account they sign in with: their work email, and a password only its
    ///     hash of which is kept.
    /// </summary>
    /// <remarks>
    ///     The email in the form every identity action looks it up in (<c>LocalIdentity.NormalizeEmail</c>),
    ///     and the key composed from it as <c>RegisterUser</c> composes it — the local store is the identity
    ///     provider, whoever signs the token — so a provisioned account and a registered one cannot be told
    ///     apart by the code that reads them.
    /// </remarks>
    internal void OpenAccount(string passwordHash)
    {
        var email = LocalIdentity.NormalizeEmail(WorkEmail);
        Identity = new LocalIdentity
        {
            Email = email,
            PasswordHash = passwordHash,
            ExternalIdentityKey = ExternalIdentityKey.Compose(LocalIdentity.Provider, email)!,
            ProvisionSource = ProvisionSource.Manual,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
    }

    /// <summary>
    ///     The reference the rest of the system knows the employee by — the token's subject, so what the
    ///     rows they write and the audit trail record — created on first use, and the same one until the
    ///     employee is erased.
    /// </summary>
    internal ValueTask<string> ReferenceAsync(ISubjectRegistry subjects, CancellationToken ct) =>
        subjects.GetOrCreateReferenceAsync(nameof(Employee), EmployeeNumber, ct);

    /// <summary>
    ///     A preference is one of the languages the application speaks: any other would be stored and
    ///     change nothing.
    /// </summary>
    internal ValidationError CheckLanguage() =>
        PreferredCulture is null || Languages.IsSupported(PreferredCulture)
            ? ValidationError.Valid
            : ValidationError.For(nameof(PreferredCulture), T.Validation.Employee.UnsupportedLanguage,
                ("supported", Languages.Listed));

    /// <summary>
    ///     Revokes every token issued to the employee: the next request with one of them is refused, and
    ///     they sign in again to get a token that says what is true now.
    /// </summary>
    internal void RevokeSessions()
    {
        if (Identity is not null)
            Identity.SecurityStamp = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    ///     The employee has left: the account stops authenticating, and the sessions already issued end.
    /// </summary>
    /// <remarks>The password is kept: a termination recorded by mistake is undone with <see cref="Reinstate" />.</remarks>
    internal void Terminate()
    {
        if (Identity is null)
            return;

        Identity.IsActive = false;
        RevokeSessions();
    }

    /// <summary>The account works again, with the password it had.</summary>
    /// <remarks>
    ///     Not an account erasure removed: there is no account left, and the person it was opened for is
    ///     no longer on record.
    /// </remarks>
    internal void Reinstate()
    {
        if (Identity is { PasswordHash.Length: > 0 })
            Identity.IsActive = true;
    }
}
