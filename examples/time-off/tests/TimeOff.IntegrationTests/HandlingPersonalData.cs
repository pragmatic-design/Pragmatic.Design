using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Invoker;
using Pragmatic.Pipeline;
using Pragmatic.Privacy;
using TimeOff.Leave.Dtos;
using TimeOff.Leave.Entities;
using TimeOff.Leave.Employees.Actions;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     The employees' personal data is classified, and each employee can have it exported; the
///     classification is in the register of processing activities. Erasure is <see cref="ErasingAnEmployee" />.
/// </summary>
public sealed class HandlingPersonalData(PostgresFixture database) : TimeOffTestBase(database)
{
    private const string EmployeeCategory = "TimeOff.Leave.Entities.Employee";
    private const string LeaveRequestCategory = "TimeOff.Leave.Entities.LeaveRequest";

    /// <summary>
    ///     The subject is what the application records as the one who acted — every row's author, every
    ///     access scope, every audit entry — so it has to be a reference and not the email: the trail is
    ///     append-only, and no erasure could reach an email written into it.
    /// </summary>
    [Fact]
    public async Task TheSessionToken_NamesTheEmployeeByAReference_NotByTheirEmail()
    {
        var employee = await HireAsync();

        var session = await ReadJsonAsync(await PostSignInAsync(employee.Account.WorkEmail, employee.Account.Password));
        var subject = ClaimsOf(session.GetProperty("token").GetString()!).GetProperty("sub").GetString()!;

        subject.Should().MatchRegex("^[0-9a-f]{32}$");
        subject.Should().NotContain(employee.Account.WorkEmail);
    }

    [Fact]
    public async Task AnEmployee_GetsTheirPersonalData_EveryClassifiedField()
    {
        var employee = await HireAsync();
        var member = await SignInAsync(employee.Account);
        var sickLeave = await DefineKindAsync(usesAllowance: false);
        await ReadJsonAsync(await member.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = sickLeave, from = Day(28), to = Day(28), reason = "A reason only the export shows" }));

        var export = await ReadJsonAsync(await member.GetAsync("/api/me/personal-data"));

        var profile = Records(export, EmployeeCategory).Should().ContainSingle().Which;
        profile.GetProperty("WorkEmail").GetString().Should().Be(employee.Account.WorkEmail);
        profile.GetProperty("FullName").GetString().Should().Be(employee.Account.FullName);
        profile.GetProperty("EmployeeNumber").GetString().Should().StartWith("EMP-");

        // The request is in the export, with what the extractor can read of it.
        Records(export, LeaveRequestCategory).Should().NotBeEmpty();

        // ⚠️ And the reason is not, deliberately: it is encrypted under this employee's own key, and
        // the generated extractor performs no cryptography — for the same reason the value converter
        // does not, since reading has three outcomes and one of them is "this subject was erased".
        // Returning the ciphertext would be worse than leaving it out, and an empty string would say
        // the employee gave no reason. It is read through its own endpoint, asserted below.
        export.GetRawText().Should().NotContain("A reason only the export shows",
            "a protected field is not carried by the generated export");
    }

    /// <summary>
    ///     The reason is still the employee's to read — through the endpoint that can say which of the
    ///     three things happened.
    /// </summary>
    /// <remarks>
    ///     Without this the assertion above would say the data is simply missing from a subject access
    ///     request, which would be a defect rather than a design: what is protected is still available
    ///     to the person it is about, by a read that can answer "erased".
    /// </remarks>
    [Fact]
    public async Task TheProtectedReason_IsReadThroughItsOwnEndpoint()
    {
        var employee = await HireAsync();
        var member = await SignInAsync(employee.Account);
        var sickLeave = await DefineKindAsync(usesAllowance: false);
        var request = (await ReadJsonAsync(await member.PostAsJsonAsync("/api/leave-requests",
                new { absenceKindId = sickLeave, from = Day(28), to = Day(28), reason = "Surgery on the left knee" })))
            .GetProperty("id").GetGuid();

        var read = await ReadJsonAsync(await member.GetAsync($"/api/leave-requests/{request}/reason"));

        read.GetProperty("outcome").GetString().Should().Be("Given");
        read.GetProperty("reason").GetString().Should().Be("Surgery on the left knee");
    }

    /// <summary>The control: the export is the caller's, and holds nobody else's data.</summary>
    /// <summary>
    ///     The export loads the signed-in employee with <c>[LoadCurrentUser]</c>: an account the
    ///     application knows no employee for is answered 404 by the invoker, before the export runs.
    /// </summary>
    /// <remarks>
    ///     In process, because over HTTP there is no such account here: every account is an employee, and one
    ///     who left is signed out. The principal is authenticated and names nobody.
    /// </remarks>
    [Fact]
    public async Task AnAccountWithNoEmployee_IsNotFound_BeforeTheExportRuns()
    {
        var (status, _) = await ExportAsAsync(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "Test"));

        status.Should().Be(404);
    }

    /// <summary>The control: nobody signed in is a 401, not a 404.</summary>
    [Fact]
    public async Task NobodySignedIn_IsUnauthorized()
    {
        var (status, _) = await ExportAsAsync(new ClaimsIdentity());

        status.Should().Be(401);
    }

    [Fact]
    public async Task TheExport_HoldsOnlyTheCallersData()
    {
        var one = await HireAsync();
        var other = await HireAsync();

        var export = await ReadJsonAsync(await (await SignInAsync(one.Account)).GetAsync("/api/me/personal-data"));

        export.GetRawText().Should().Contain(one.Account.WorkEmail).And.NotContain(other.Account.WorkEmail);
    }

    [Fact]
    public async Task TheRegister_ListsWhatEachRecordHolds_AndHowItIsErased()
    {
        var hr = await SignInAsHrAsync();

        var register = await ReadJsonAsync(await hr.GetAsync("/api/compliance/processing-register"));

        register.GetProperty("controllerName").GetString().Should().Be("Time off");

        var employee = Activity(register, EmployeeCategory);
        employee.GetProperty("isDataSubject").GetBoolean().Should().BeTrue();
        Strings(employee.GetProperty("categories")).Should().Contain("Identity").And.Contain("Contact").And.Contain("Behavioural");
        employee.GetProperty("erasure").GetProperty("FullName").GetString().Should().Be("Anonymize");
        employee.GetProperty("erasure").GetProperty("WorkEmail").GetString().Should().Be("Pseudonymize");
        employee.GetProperty("purpose").GetString().Should().NotBeNullOrWhiteSpace();

        var request = Activity(register, LeaveRequestCategory);
        request.GetProperty("isDataSubject").GetBoolean().Should().BeFalse();
        Strings(request.GetProperty("categories")).Should().Contain("Special");
    }

    /// <summary>
    ///     An employee has their pseudonym from the moment HR registers them, not from the
    ///     first time they sign in.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Asserted on the <b>registry</b>, deliberately, and not on an incident or an export.
    ///         A reference allocated on first sight — first successful sign-in, an export, an erasure —
    ///         would leave an account nobody had used yet unattributable, which is exactly the account an
    ///         attacker works on (the per-subject rule of <see cref="NoticingAnAttack" /> could not see
    ///         the burst). Observing it only through those two mechanisms would make a failure here look
    ///         like theirs.
    ///     </para>
    ///     <para>
    ///         The subject has to exist <b>before</b> the attack and not because of it: the bridge that
    ///         attributes a failed sign-in looks up and never allocates, since its input is an address a
    ///         stranger typed and pseudonymising that would let anyone fill the registry by guessing.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AnEmployeeWhoHasNeverSignedIn_AlreadyHasTheirPseudonym()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();

        var number = (await ReadJsonAsync(await hr.GetAsync($"/api/employees/{employee.Id}")))
            .GetProperty("employeeNumber").GetString()!;

        (await PseudonymOfAsync(number)).Should().NotBeNullOrEmpty(
            "the employee entered the system when HR registered them, and nothing they do later is "
            + "what makes them attributable");
    }

    /// <summary>
    ///     The control: the registry does not invent a reference for a number nobody was hired under.
    /// </summary>
    /// <remarks>
    ///     Without it, "the employee has a pseudonym" is satisfied by allocating one for every string
    ///     anybody asks about — which is the failure mode the bridge is careful to avoid, moved one
    ///     layer down.
    /// </remarks>
    [Fact]
    public async Task ANumberNobodyWasHiredUnder_HasNoPseudonym()
    {
        await HireAsync();

        (await PseudonymOfAsync("EMP-99999")).Should().BeNull();
    }

    private async Task<string?> PseudonymOfAsync(string employeeNumber)
    {
        using var scope = Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISubjectRegistry>()
            .FindReferenceAsync(nameof(Employee), employeeNumber);
    }

    /// <summary>
    ///     What the employee's record <em>owns</em> is part of what the employee holds.
    /// </summary>
    /// <remarks>
    ///     The account is an owned <c>LocalIdentity</c>, and the address it signs in with is classified
    ///     on that type. ⚠️ The register and the erasure plan follow owned types too. Stopping at the
    ///     employee's own face would leave the column masked in the logs — <c>DeclaredRedactor</c> walks
    ///     paths — and absent from the register, which is the document that states what is held: two
    ///     mechanisms reading the same declaration and disagreeing, with the silent one deciding whether
    ///     the build passes.
    /// </remarks>
    [Fact]
    public async Task TheRegister_ListsWhatTheOwnedAccountHolds_NotOnlyTheEmployeesOwnFace()
    {
        var hr = await SignInAsHrAsync();

        var register = await ReadJsonAsync(await hr.GetAsync("/api/compliance/processing-register"));
        var employee = Activity(register, EmployeeCategory);

        employee.GetProperty("erasure").GetProperty("Identity.Email").GetString().Should().Be("Null");
    }

    /// <summary>
    ///     The same descent in the export — and the control that it carries no credential.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The control is the point. The generated extractor projects <b>every</b> classified field,
    ///     so classifying the password hash or the security stamp as personal data would hand them back
    ///     over HTTP to anyone who asks for their own data. They are declared <c>[NotPersonalData]</c>
    ///     with the reason on <c>LocalIdentity</c> instead, and what removes them is the erasure
    ///     removing the account — which <see cref="ErasingAnEmployee" /> asserts.
    /// </remarks>
    [Fact]
    public async Task AnEmployeesExport_CarriesTheirAccountsAddress_AndNoneOfItsSecrets()
    {
        var employee = await HireAsync();
        var member = await SignInAsync(employee.Account);

        var export = await ReadJsonAsync(await member.GetAsync("/api/me/personal-data"));

        var profile = Records(export, EmployeeCategory).Should().ContainSingle().Which;
        profile.GetProperty("Identity.Email").GetString().Should()
            .Be(employee.Account.WorkEmail.ToLowerInvariant());

        foreach (var secret in new[]
                 {
                     "Identity.PasswordHash", "Identity.SecurityStamp",
                     "Identity.ResetToken", "Identity.EmailVerificationToken"
                 })
            profile.TryGetProperty(secret, out _).Should()
                .BeFalse($"'{secret}' is a credential, and an access response is not where one belongs");
    }

    /// <summary>
    ///     What an operation loads is in the register with nothing declared on it: the submission carries no
    ///     <c>[ProcessesData]</c>, and a decision — which loads the signed-in manager — is in the register
    ///     because the generator reads its loads.
    /// </summary>
    [Fact]
    public async Task TheRegister_ListsWhatAnOperationLoads_WithNothingDeclared()
    {
        var hr = await SignInAsHrAsync();

        var register = await ReadJsonAsync(await hr.GetAsync("/api/compliance/processing-register"));
        var operations = register.GetProperty("operations").EnumerateArray()
            .Select(o => (Operation: o.GetProperty("operationType").GetString(), Entity: o.GetProperty("entityType").GetString()))
            .ToList();

        operations.Should().Contain(("TimeOff.Leave.LeaveRequests.Actions.SubmitLeaveRequestAction", EmployeeCategory));
        operations.Should().Contain(("TimeOff.Leave.LeaveRequests.Actions.SubmitLeaveRequestAction", LeaveRequestCategory));
        operations.Should().Contain(("TimeOff.Leave.LeaveRequests.Mutations.ApproveLeaveRequestMutation", EmployeeCategory));
    }

    private static List<JsonElement> Records(JsonElement export, string category) =>
    [
        .. export.GetProperty("categories").EnumerateArray()
            .Where(c => c.GetProperty("category").GetString() == category)
            .SelectMany(c => c.GetProperty("records").EnumerateArray())
    ];

    private static JsonElement Activity(JsonElement register, string entityType) =>
        register.GetProperty("activities").EnumerateArray()
            .Single(a => a.GetProperty("entityType").GetString() == entityType);

    private static List<string?> Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString())];

    /// <summary>The token's payload: the second of its three base64url parts.</summary>
    private static JsonElement ClaimsOf(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.Clone();
    }

    /// <summary>Runs the export in process as <paramref name="identity" />, and answers its status.</summary>
    private async Task<(int Status, string Code)> ExportAsAsync(ClaimsIdentity identity)
    {
        using var scope = Services.CreateScope();
        var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        http.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = scope.ServiceProvider
        };
        try
        {
            var export = scope.ServiceProvider
                .GetRequiredService<IDomainActionInvoker<ExportMyPersonalDataAction, PersonalDataExportDto>>();
            using (scope.ServiceProvider.GetRequiredService<ICallContext>().EnterInternalCall())
            {
                var result = await export.InvokeAsync(new ExportMyPersonalDataAction());
                result.IsFailure.Should().BeTrue("the account has no employee to export");
                return (result.Error.StatusCode, result.Error.Code);
            }
        }
        finally
        {
            http.HttpContext = null;
        }
    }
}
