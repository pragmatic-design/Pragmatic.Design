using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     A test against the running application: a host per test class, and HTTP clients to it.
/// </summary>
/// <remarks>
///     A test authenticates the way a client does — it signs in and sends the bearer token it got back.
///     There is no test-only authentication anywhere: what these tests reach, a client reaches.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public abstract class TimeOffTestBase(PostgresFixture database) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private TimeOffWebFactory _factory = null!;

    /// <summary>A client that has not signed in.</summary>
    protected HttpClient Client { get; private set; } = null!;

    protected IServiceProvider Services => _factory.Services;

    /// <summary>Where the invitations and password resets arrive.</summary>
    protected TestMailbox Mailbox => _factory.Mailbox;

    /// <summary>The queries the application sent to the database.</summary>
    protected SqlCapture Sql => _factory.Sql;

    public async Task InitializeAsync()
    {
        _factory = new TimeOffWebFactory(await ConnectionStringAsync(database), Settings, ConfigureServices);
        Client = _factory.CreateClient();
        _clients.Add(Client);
    }

    /// <summary>Configuration a class needs on top of the suite's, applied after it.</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>();

    /// <summary>Services a class replaces on top of the suite's, registered after them.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    /// <summary>The database the host runs on: the one every class shares, unless a class needs its own.</summary>
    protected virtual Task<string> ConnectionStringAsync(PostgresFixture database) =>
        Task.FromResult(database.ConnectionString);

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
            client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>
    ///     <c>POST /identity/local/sign-in</c>, and the response as it came.
    /// </summary>
    protected Task<HttpResponseMessage> PostSignInAsync(string email, string password) =>
        Client.PostAsJsonAsync("/identity/local/sign-in", new { email, password });

    /// <summary>
    ///     Signs in and returns a client that sends the token on every request.
    /// </summary>
    protected async Task<HttpClient> SignInAsync(TestAccount account)
    {
        var session = await ReadJsonAsync(await PostSignInAsync(account.WorkEmail, account.Password));
        return ClientWithToken(session.GetProperty("token").GetString()!);
    }

    /// <summary>The first HR administrator, signed in.</summary>
    protected Task<HttpClient> SignInAsHrAsync() => SignInAsync(TestAccounts.FirstAdministrator);

    /// <summary>
    ///     HR registers an employee, and the employee accepts the invitation by choosing a password —
    ///     every step over HTTP, as the two people would do it.
    /// </summary>
    /// <remarks>Each call is a new person: the work email is unique, since the database is shared.</remarks>
    protected async Task<HiredEmployee> HireAsync(string role = "Employee", Guid? teamId = null)
    {
        var hr = await SignInAsHrAsync();
        var email = $"{Guid.NewGuid():N}@time-off.test";
        var fullName = $"Employee {email[..8]}";

        var created = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/employees",
            new { fullName, workEmail = email, hiredOn = "2024-03-01", role, teamId }));

        var password = $"Chosen-{Guid.NewGuid():N}"[..20] + "!1";
        await ReadSuccessAsync(await AcceptInvitationAsync(email, password));

        return new HiredEmployee(new TestAccount(fullName, email, password), created.GetProperty("id").GetGuid());
    }

    /// <summary>HR defines a kind of absence with a code of its own, and returns its id.</summary>
    protected async Task<Guid> DefineKindAsync(string unit = "Days", bool usesAllowance = true)
    {
        var hr = await SignInAsHrAsync();
        var kind = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/absence-kinds", new
        {
            code = $"KIND_{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            name = new Dictionary<string, string> { ["en-US"] = "A kind", ["it-IT"] = "Un tipo" },
            unit,
            usesAllowance
        }));

        return kind.GetProperty("id").GetGuid();
    }

    /// <summary>
    ///     <c>POST /identity/local/reset-password/confirm</c> with the token the mailbox received.
    /// </summary>
    protected Task<HttpResponseMessage> AcceptInvitationAsync(string email, string newPassword) =>
        Client.PostAsJsonAsync("/identity/local/reset-password/confirm",
            new { email, token = Mailbox.LatestTokenFor(email), newPassword });

    /// <summary>
    ///     A client that sends <paramref name="token" /> as its bearer token.
    /// </summary>
    protected HttpClient ClientWithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _clients.Add(client);
        return client;
    }

    /// <summary>
    ///     The response body as JSON, or a failure that carries the body: a test that fails saying only
    ///     "500" has to be run again to learn why.
    /// </summary>
    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)response.StatusCode} {response.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>A success with no body to read, or a failure that carries the body.</summary>
    protected static async Task ReadSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException(
                $"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
