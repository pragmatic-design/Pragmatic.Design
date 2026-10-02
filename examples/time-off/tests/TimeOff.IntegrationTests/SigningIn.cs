using System.Net;
using System.Net.Http.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;

namespace TimeOff.IntegrationTests;

/// <summary>
///     An employee signs in with their work email and password and gets a bearer token;
///     nothing that needs one answers without it.
/// </summary>
public sealed class SigningIn(PostgresFixture database) : TimeOffTestBase(database)
{
    [Fact]
    public async Task TheFirstAdministrator_SignsIn_AndGetsATokenThatHasNotExpired()
    {
        var account = TestAccounts.FirstAdministrator;

        var session = await ReadJsonAsync(await PostSignInAsync(account.WorkEmail, account.Password));

        session.GetProperty("token").GetString()!.Split('.').Should().HaveCount(3, "a signed JWT has three parts");
        session.GetProperty("expiresAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task TheWorkEmail_IsNotCaseSensitive()
    {
        var account = TestAccounts.FirstAdministrator;

        var session = await ReadJsonAsync(await PostSignInAsync(account.WorkEmail.ToUpperInvariant(), account.Password));

        session.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AWrongPassword_IsRefused_With401()
    {
        var response = await PostSignInAsync(TestAccounts.FirstAdministrator.WorkEmail, "Not-the-password-1!");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    ///     The same answer as a wrong password: whether an account exists is not something a stranger can
    ///     learn by asking.
    /// </summary>
    [Fact]
    public async Task AnUnknownEmail_IsRefused_TheSameWay()
    {
        var response = await PostSignInAsync("nobody@time-off.test", "Whatever-Pa55!");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithoutAToken_AnOperationThatNeedsOne_Answers401()
    {
        var response = await Client.PostAsJsonAsync("/identity/local/change-password",
            new { currentPassword = "Anything-Pa55!", newPassword = "Something-Else-Pa55!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    ///     A password change revokes every token issued before it: whoever had one — a stolen laptop, a
    ///     shared browser — loses the session with the old password.
    /// </summary>
    [Fact]
    public async Task ChangingThePassword_RevokesTheTokensIssuedBefore()
    {
        var hired = await HireAsync();
        var before = await SignInAsync(hired.Account);

        await ReadSuccessAsync(await before.PostAsJsonAsync("/identity/local/change-password",
            new { currentPassword = hired.Account.Password, newPassword = "A-Brand-New-Pa55!" }));

        (await before.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the token carries the security stamp the change rotated");
        (await PostSignInAsync(hired.Account.WorkEmail, hired.Account.Password)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "the old password is gone");
        var after = await SignInAsync(hired.Account with { Password = "A-Brand-New-Pa55!" });
        (await after.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    ///     A token says what its holder may do; when HR changes that, the token stops being true, and
    ///     stops being accepted.
    /// </summary>
    [Fact]
    public async Task ANewRole_RevokesTheSessionsIssuedBefore()
    {
        var hired = await HireAsync();
        var before = await SignInAsync(hired.Account);
        (await before.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK, "the control: the token works");

        var hr = await SignInAsHrAsync();
        await ReadJsonAsync(await hr.PutAsJsonAsync($"/api/employees/{hired.Id}", new { role = "Manager" }));

        (await before.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var profile = await ReadJsonAsync(await (await SignInAsync(hired.Account)).GetAsync("/api/me"));
        profile.GetProperty("role").GetString().Should().Be("Manager");
    }

    /// <summary>
    ///     Naming someone a team's manager changes what their token should say, so the ones they hold stop
    ///     being accepted.
    /// </summary>
    /// <remarks>
    ///     The manager is loaded by <c>[LoadEntity]</c> on <c>CreateTeamMutation</c> — through the unit of
    ///     work the mutation saves, which is what makes the revocation reach the database.
    /// </remarks>
    [Fact]
    public async Task ManagingANewTeam_RevokesTheSessionsIssuedBefore()
    {
        var manager = await HireAsync(role: "Manager");
        var before = await SignInAsync(manager.Account);
        (await before.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK, "the control: the token works");

        var hr = await SignInAsHrAsync();
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }));

        (await before.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await (await SignInAsync(manager.Account)).GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A manager who does not exist is the answer, before anything is written.</summary>
    [Fact]
    public async Task ATeamForAManagerWhoDoesNotExist_Answers404()
    {
        var hr = await SignInAsHrAsync();

        var response = await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A rename is not a new role: the sessions survive it.</summary>
    [Fact]
    public async Task ARename_LeavesTheSessionsAlone()
    {
        var hired = await HireAsync();
        var before = await SignInAsync(hired.Account);

        var hr = await SignInAsHrAsync();
        await ReadJsonAsync(await hr.PutAsJsonAsync($"/api/employees/{hired.Id}", new { fullName = "Renamed Person" }));

        (await before.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AForgedToken_Answers401()
    {
        var forged = ClientWithToken("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJsb2NhbHxoci5hZG1pbkB0aW1lLW9mZi50ZXN0In0.c2lnbmF0dXJl");

        var response = await forged.PostAsJsonAsync("/identity/local/change-password",
            new { currentPassword = "Anything-Pa55!", newPassword = "Something-Else-Pa55!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
