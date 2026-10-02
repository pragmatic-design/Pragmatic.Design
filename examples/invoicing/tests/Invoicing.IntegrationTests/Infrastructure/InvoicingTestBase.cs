using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Invoicing.IntegrationTests.Infrastructure;

/// <summary>
///     A test against the running application: a host per test class, and HTTP clients to it.
/// </summary>
[Collection(IntegrationCollection.Name)]
public abstract class InvoicingTestBase(PostgresFixture database) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private InvoicingWebFactory _factory = null!;

    /// <summary>A client with no token.</summary>
    protected HttpClient Client { get; private set; } = null!;

    protected IServiceProvider Services => _factory.Services;

    /// <summary>The statements the application sent to the database.</summary>
    protected SqlCapture Sql => _factory.Sql;

    /// <summary>What the application stored, and how much of it. See the remark on the factory.</summary>
    protected Pragmatic.Storage.InMemory.InMemoryFileStorage Files => _factory.Files;

    /// <summary>
    ///     The database the host runs on. A test that reads it directly asks the base: taking the fixture
    ///     again in a derived primary constructor captures it twice (CS9107).
    /// </summary>
    protected string ConnectionString => database.ConnectionString;

    public async Task InitializeAsync()
    {
        _factory = new InvoicingWebFactory(await ConnectionStringAsync(database), Settings, ConfigureServices);
        Client = _factory.CreateClient();
        _clients.Add(Client);
    }

    /// <summary>The database the host runs on: the one every class shares, unless a class needs its own.</summary>
    protected virtual Task<string> ConnectionStringAsync(PostgresFixture shared)
        => Task.FromResult(shared.ConnectionString);

    /// <summary>
    ///     A client that calls as <paramref name="user" /> of <paramref name="tenant" />, with a token the
    ///     provider signed — the way a client does it. There is no test-only authentication anywhere, and no
    ///     tenant header: what these tests reach, a real caller reaches.
    /// </summary>
    /// <param name="user">Who is calling.</param>
    /// <param name="tenant">
    ///     The company, as the token's claim. Null mints a token with no tenant: the shape onboarding is
    ///     called with, and the one every tenant-bound route refuses with 400.
    /// </param>
    /// <param name="audience">Another audience than this application's, to be refused.</param>
    /// <param name="issuer">Another issuer than the provider, to be refused.</param>
    protected HttpClient As(TestUser user, string? tenant = null, string? audience = null, string? issuer = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestIdentityProvider.Token(user, tenant, audience, issuer));
        _clients.Add(client);
        return client;
    }

    /// <summary>
    ///     Onboards a company and returns its slug — a new one each time, because the suite shares one
    ///     database and a slug is unique across the whole service.
    /// </summary>
    /// <remarks>
    ///     Called with a token that carries no tenant, which is the only way in before the company exists:
    ///     the route is <c>[TenantAgnostic]</c>, and that is what this exercises on every use.
    /// </remarks>
    protected async Task<string> OnboardAsync(string prefix = "acme")
    {
        var slug = $"{prefix}-{Guid.NewGuid():N}"[..20];

        await ReadSuccessAsync(await As(TestUsers.PlatformAdministrator)
            .PostAsJsonAsync("api/organizations/onboard", new
            {
                slug,
                legalName = $"{prefix} S.p.A.",
                invoiceNumberPrefix = prefix.ToUpperInvariant()[..Math.Min(prefix.Length, 10)],
                // The address its reminders are sent from — each company's own, which is what the sweep
                // asserts it does not confuse with another's.
                senderEmail = $"billing@{slug}.test",
            }));

        return slug;
    }

    /// <summary>A success with no body to read, or a failure that carries the body.</summary>
    protected static async Task ReadSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException(
                $"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>Configuration a class needs on top of the suite's, applied after it.</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>();

    /// <summary>Services a class replaces on top of the suite's, registered after them.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
            client.Dispose();
        await _factory.DisposeAsync();
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
}
