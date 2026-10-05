using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using TimeOff.Leave.Employees.Actions;
using TimeOff.Leave.Entities;

namespace TimeOff.IntegrationTests;

/// <summary>
///     A field classified as personal data does not reach the logs, whichever provider is writing them.
/// </summary>
/// <remarks>
///     <para>
///         The classification is already here — <c>[PersonalData]</c> on <see cref="Employee" />, which the
///         export and the processing register read (<see cref="HandlingPersonalData" />). What nothing
///         asserted is the third thing it buys: the generator emits a compile-time redaction map from the
///         same attributes, and the generated host wraps the container's <c>ILoggerFactory</c> with it, so
///         a classified member is masked before any provider formats it.
///     </para>
///     <para>
///         ⚠️ That whole path was reachable and unexercised. A redaction that stopped working would change
///         nothing a reader of this application could see: the entries are still written, the shape is
///         still right, and the only difference is what is inside them. It is the configuration that lets
///         a mechanism quietly stop running.
///     </para>
///     <para>
///         ⚠️ <b>Declared redaction is not the pattern redactor.</b> <c>PersonalDataRedactor</c> catches
///         what looks sensitive — an e-mail, an IBAN, a long digit run — is a heuristic, and sits behind
///         <c>Privacy.EnableRedaction</c>. This one catches what was <em>declared</em>, has no environment
///         qualifier, and is what these tests are about.
///     </para>
///     <para>
///         The keys are camelCase: the redactor writes a redacted value the way the JSON providers write
///         every other complex value, and the way the generated JSON context writes it under Native AOT.
///     </para>
/// </remarks>
public sealed class WhatTheLogsMayNotSay(PostgresFixture database) : TimeOffTestBase(database)
{
    private readonly LogCapture _log = new();

    /// <summary>What the employee is: masked, both the name and the address.</summary>
    [Fact]
    public async Task AClassifiedField_IsMaskedBeforeAnyProviderSeesIt()
    {
        var hired = await HireAsync();

        var entry = await LoggedEmployeeAsync(hired.Id);

        entry.Should().Contain("\"workEmail\":\"[redacted]\"", "WorkEmail is [PersonalData(Contact)]");
        entry.Should().Contain("\"fullName\":\"[redacted]\"", "FullName is [PersonalData(Identity)]");
        entry.Should().NotContain(hired.Account.FullName, "and the value itself is gone");
    }

    /// <summary>
    ///     The secrets the owned identity record holds do not reach a provider.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Employee.Identity</c> is an owned <c>LocalIdentity</c>, the framework's own type, and
    ///         it carries the password hash, both bearer tokens and the security stamp. Two things keep
    ///         them out, and neither is enough alone: the four secrets are <c>[NotLogged]</c>, and the
    ///         map carries paths the redactor walks — a walk over the payload's <b>top-level keys</b>
    ///         would never reach them, classified or not.
    ///     </para>
    ///     <para>
    ///         ⚠️ Asserted on the <b>value</b>, not on the member name: the mask keeps the name, so
    ///         "PasswordHash is not in the entry" would fail on a correct entry and pass on one that
    ///         had dropped the member entirely.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheSecretsInTheOwnedIdentityRecord_DoNotReachAProvider()
    {
        var hired = await HireAsync();

        var entry = await LoggedEmployeeAsync(hired.Id);

        entry.Should().NotContain("$2",
            "a bcrypt hash starts with $2, and Identity.PasswordHash is [NotLogged]");
        entry.Should().Contain("\"passwordHash\":\"[redacted]\"",
            "masked rather than omitted, so the entry does not claim the field was unset");
        entry.Should().Contain("\"securityStamp\":\"[redacted]\"");
    }

    /// <summary>
    ///     The address is masked one level down too, where the owned identity record carries it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Three separate things have to be true</b> for the same address not to appear twice
    ///         — masked on the entity's face and in clear below it: the redactor descends (the map
    ///         carries paths), <c>LocalIdentity.Email</c> is classified, and classifying it is allowed
    ///         at all — <b>PRAG2900</b> does not refuse a library that declares personal data, because
    ///         reachability to a <c>[DataSubject]</c> cannot be decided within the library's own
    ///         compilation: the subject is always in the application.
    ///     </para>
    ///     <para>
    ///         The control below — <c>WhatNobodyClassified_IsStillWritten</c> — is
    ///         what keeps this from being satisfied by an entry that says nothing at all.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheAddressInTheOwnedIdentityRecord_IsMaskedToo()
    {
        var hired = await HireAsync();

        var entry = await LoggedEmployeeAsync(hired.Id);

        entry.Should().NotContain(hired.Account.WorkEmail,
            "the same address the face masked must not go out below it, through Identity.Email");
        entry.Should().Contain("\"email\":\"[redacted]\"",
            "masked rather than omitted, on the owned record as on the face");
    }

    /// <summary>
    ///     And the column the owned record <em>inherits</em> is masked as well.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>LocalIdentity</c> extends <c>IdentityRecord</c>, which declares
    ///         <c>ExternalIdentityKey</c> — the composed <c>{issuer}|{subject}</c> key whose subject
    ///         half, for a local sign-in, <b>is the address</b>. Both walks read what a type declares,
    ///         so a base class was invisible to them: this column was classified by nothing, masked by
    ///         nothing, and absent from the register and the erasure plan.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>The neighbouring test could not see it.</b>
    ///         <see cref="TheAddressInTheOwnedIdentityRecord_IsMaskedToo" /> asserts the raw address is
    ///         not in the entry, and the composed key holds it <b>percent-escaped</b> — so the leak went
    ///         out beside an assertion written to catch exactly it. That is why this one asserts the
    ///         escaped form, which is what was actually on the wire.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheKeyTheOwnedIdentityRecordInherits_IsMaskedToo()
    {
        var hired = await HireAsync();

        var entry = await LoggedEmployeeAsync(hired.Id);

        entry.Should().NotContain(Uri.EscapeDataString(hired.Account.WorkEmail),
            "the composed key escapes the address, which is the form that reached the log");
        entry.Should().Contain("\"externalIdentityKey\":\"[redacted]\"",
            "masked rather than omitted, like every other member the map names");
    }

    /// <summary>
    ///     The control: what nobody classified is still written.
    /// </summary>
    /// <remarks>
    ///     Without it, "the address is not in the entry" is satisfied by an entry that says nothing at all
    ///     — a logger that dropped the value, a serializer that threw and was swallowed, a provider that
    ///     never ran. The assertion has to be able to fail in both directions, and only a field beside the
    ///     masked ones can do that: <c>HiredOn</c> and <c>Role</c> carry no <c>[PersonalData]</c>.
    /// </remarks>
    [Fact]
    public async Task WhatNobodyClassified_IsStillWritten()
    {
        var hired = await HireAsync();

        var entry = await LoggedEmployeeAsync(hired.Id);

        entry.Should().Contain("2024-03-01", "HiredOn declares nothing and is the day HireAsync uses");
    }

    /// <summary>
    ///     The one secret this application takes in clear does not reach a provider.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>ProvisionFirstAdministratorAction</c> takes a password: the first HR administrator
    ///         is created by the host at startup from configuration, because there is nobody to
    ///         create them. It is hashed and never stored — which is exactly why
    ///         <c>[NotLogged]</c> and not <c>[PersonalData]</c>: a secret with no data subject has no
    ///         right of erasure and no place in an export, and what it needs is to stay out of the
    ///         logs.
    ///     </para>
    ///     <para>
    ///         ⚠️ An action's inputs are a typed pipeline object, which is the shape the redaction
    ///         map covers — the same channel as the entity above, reached from a different kind of
    ///         type. The control is <c>FullName</c> beside it, which declares nothing.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ThePasswordTheFirstAdministratorIsGiven_DoesNotReachAProvider()
    {
        const string secret = "correct-horse-battery-staple";

        var entry = Logged("The provisioning is {Provisioning}", new ProvisionFirstAdministratorAction
        {
            FullName = "Ada Lovelace",
            WorkEmail = "ada@timeoff.test",
            Password = secret
        });

        entry.Should().NotContain(secret, "the password is [NotLogged]");
        entry.Should().Contain("\"password\":\"[redacted]\"",
            "masked rather than omitted, so the entry does not claim no password was given");
        entry.Should().Contain("Ada Lovelace",
            "and the control: FullName declares nothing on this action and is still written");
    }

    /// <summary>Logs <paramref name="value" /> through the application's own factory, as above.</summary>
    private string Logged(string template, object value)
    {
        using var scope = Services.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        factory.AddProvider(_log);
        factory.CreateLogger("TimeOff.IntegrationTests.WhatTheLogsMayNotSay")
            .LogInformation(template, value);

        var prefix = template[..template.IndexOf('{', StringComparison.Ordinal)];
        var mine = _log.Entries.Where(e => e.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        mine.Should().ContainSingle(
            $"the application's own logger factory has to reach this provider — {_log.Entries.Count} "
            + "entries were captured in total");

        return mine[0];
    }

    /// <summary>The mask is a mask, and says the field was there.</summary>
    /// <remarks>
    ///     Omitting the member instead would read as "the field was not set", which is a different
    ///     statement about what happened, and a false one.
    /// </remarks>
    [Fact]
    public async Task TheMaskedField_IsStillNamed()
    {
        var hired = await HireAsync();

        var entry = await LoggedEmployeeAsync(hired.Id);

        entry.Should().Contain("\"workEmail\"", "the member is named, and its value is replaced");
    }

    /// <summary>
    ///     Logs the employee through the application's own <c>ILoggerFactory</c> and returns what a
    ///     provider was handed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The factory comes from the running host, so this drives the wrapping the generated host
    ///         installed — not a redactor the test built. A test that resolved <c>DeclaredRedactor</c>
    ///         and called it would prove the map is right and say nothing about whether anything uses it.
    ///     </para>
    ///     <para>
    ///         ⚠️ The provider is added <b>here</b> rather than in <c>ConfigureServices</c>, and that is
    ///         not a style choice: <c>TimeOffWebFactory</c> calls <c>ConfigureLogging</c> after
    ///         <c>ConfigureTestServices</c>, so <c>ClearProviders()</c> removes anything registered the
    ///         other way and the capture stays empty. Adding it late is also the case
    ///         <c>RedactingLoggerFactory</c> was written for — its remark names a test host doing exactly
    ///         this, which is why it wraps the factory instead of the providers.
    ///     </para>
    /// </remarks>
    private async Task<string> LoggedEmployeeAsync(Guid employeeId)
    {
        using var scope = Services.CreateScope();
        var employee = await scope.ServiceProvider
            .GetRequiredService<IRepository<Employee>>()
            .GetByIdAsync(employeeId);

        employee.Should().NotBeNull();

        var factory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        factory.AddProvider(_log);
        factory.CreateLogger("TimeOff.IntegrationTests.WhatTheLogsMayNotSay")
            .LogInformation("The employee is {Employee}", employee);

        var mine = _log.Entries.Where(e => e.StartsWith("The employee is", StringComparison.Ordinal)).ToList();

        mine.Should().ContainSingle(
            $"the application's own logger factory has to reach this provider — {_log.Entries.Count} "
            + "entries were captured in total");

        return mine[0];
    }
}
