using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>
///     Base class for all E2E integration tests.
///     Provides a pre-configured HttpClient pointing at the real Showcase app
///     running against Testcontainers PostgreSQL.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public abstract class IntegrationTestBase(PostgresFixture fixture) : IAsyncLifetime
{
    private ShowcaseWebFactory _factory = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected PostgresFixture Fixture { get; } = fixture;

    /// <summary>The host application's root service provider (for resolving DI services in tests).</summary>
    protected IServiceProvider Services => _factory.Services;

    /// <summary>
    ///     A handler routed to the in-memory test server. Lets a test wire an <see cref="HttpClient"/> built by
    ///     someone else — such as the SG-generated typed client registered through <c>AddHttpClient</c> — at the
    ///     running app instead of a real socket.
    /// </summary>
    protected HttpMessageHandler CreateServerHandler() => _factory.Server.CreateHandler();

    protected static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    ///     Default permissions for all test clients. Covers the test setup chain
    ///     (mutations, DomainActions, endpoints) except explicitly-tested denial scenarios.
    /// </summary>
    // Broad CRUD grant so secure-by-default endpoints — which now enforce their *derived* permission, not
    // just the explicitly-attributed ones — are reachable for business-logic tests. Boundary wildcards keep
    // it future-proof. CRUCIAL: this is NOT "*". CreateClientAs(...) layers these same defaults onto a
    // user, and several denial tests assert 403 for a *specific* permission the user must NOT hold
    // (billing.invoice.refund, rates.import). Those are deliberately excluded here; "*" would grant them
    // and silently defeat every denial test. Add new boundary wildcards, never "*".
    private static readonly string DefaultPermissions = string.Join(",",
        "catalog.*",                 // property/roomtype/amenity/cancellation-policy CRUD + queries
        "booking.*",                 // guest/reservation CRUD, *.view-all data-filter bypass, *.comments.* traits
        "billing.invoice.read",      // billing is enumerated (NOT billing.*) to withhold billing.invoice.refund
        "billing.invoice.create",
        "billing.invoice.update",
        "billing.invoice.view-all"); // withheld on purpose: billing.invoice.refund, rates.import (denial tests)

    /// <summary>
    ///     Whether this class needs a host of its own instead of the one the collection shares.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Sharing the host shares the DI <b>root</b>, so singletons live for the whole run. Most
    ///         tests never notice — scoped services are per request and the database was already shared
    ///         — but a class that reads or mutates a singleton store, or that needs a cold cache,
    ///         does. Those override this and say why, which costs one host and keeps the reason where
    ///         the next reader will look.
    ///     </para>
    ///     <para>
    ///         ⚠️ It is deliberately opt-<em>out</em>. Opting in would have left the default at 585
    ///         hosts, which is the thing being fixed, and would have made "nobody got round to it"
    ///         indistinguishable from "this one needs it".
    ///     </para>
    /// </remarks>
    protected virtual bool NeedsItsOwnHost => false;

    private bool _ownsFactory;

    public Task InitializeAsync()
    {
        if (NeedsItsOwnHost)
        {
            _factory = new ShowcaseWebFactory(Fixture);
            _ownsFactory = true;
        }
        else
        {
            _factory = Fixture.SharedHost;
        }

        Client = _factory.CreateClient();

        // Default headers: tenant + identity for auth
        Client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        Client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
        Client.DefaultRequestHeaders.Add("X-User-Name", "Integration Test");

        // Default permissions — covers DomainAction and endpoint-level auth for the test setup chain.
        // Excludes: billing.invoice.refund, rates.import (tested for 403 in AuthorizationTests).
        Client.DefaultRequestHeaders.Add("X-User-Permissions", DefaultPermissions);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();

        // The shared host outlives the test; only a host this class asked for is its to dispose.
        if (_ownsFactory)
            await _factory.DisposeAsync().ConfigureAwait(false);
    }

    // =========================================================================
    // HTTP Helpers
    // =========================================================================

    protected async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Client.PostAsJsonAsync(url, body, JsonOptions).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions).ConfigureAwait(false);
        return result!;
    }

    protected async Task<HttpResponseMessage> PostAsync(string url, object body)
    {
        return await Client.PostAsJsonAsync(url, body, JsonOptions).ConfigureAwait(false);
    }

    protected async Task<T> GetAsync<T>(string url)
    {
        var response = await Client.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions).ConfigureAwait(false);
        return result!;
    }

    protected async Task<HttpResponseMessage> GetRawAsync(string url)
    {
        return await Client.GetAsync(url).ConfigureAwait(false);
    }

    protected async Task<HttpResponseMessage> PutAsync(string url, object body)
    {
        return await Client.PutAsJsonAsync(url, body, JsonOptions).ConfigureAwait(false);
    }

    protected async Task<HttpResponseMessage> DeleteAsync(string url)
    {
        return await Client.DeleteAsync(url).ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates a new HttpClient with custom identity headers.
    ///     Useful for testing row-level security with different users.
    ///     Includes the same default permissions as the main Client so that
    ///     DomainAction/Endpoint authorization checks pass for setup helpers.
    /// </summary>
    /// <summary>
    ///     A client whose requests carry an external identity key, so anything that resolves the user
    ///     entity from the database can find it.
    /// </summary>
    /// <remarks>
    ///     <c>X-User-Id</c> alone is not enough for that: HeaderUserMiddleware writes it as
    ///     <c>UserIdClaimType</c> and emits no <c>iss</c>, so the key would be composed from nothing.
    ///     Every feature behind a user resolver — the per-user culture among them — is untestable end to
    ///     end without this.
    /// </remarks>
    protected HttpClient CreateClientForIdentity(string externalIdentityKey,
        string userName = "Test User", string tenantId = "test-tenant")
    {
        var client = CreateClientAs(externalIdentityKey, userName, tenantId);
        client.DefaultRequestHeaders.Add("X-User-External-Key", externalIdentityKey);
        return client;
    }

    protected HttpClient CreateClientAs(string userId, string userName = "Test User",
        string tenantId = "test-tenant")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        client.DefaultRequestHeaders.Add("X-User-Id", userId);
        client.DefaultRequestHeaders.Add("X-User-Name", userName);
        client.DefaultRequestHeaders.Add("X-User-Permissions", DefaultPermissions);
        return client;
    }

    /// <summary>
    ///     A client that is a <b>named</b> user and holds only the permissions given.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Neither neighbour does both, and a row-filter test needs both: <c>CreateClientAs</c>
    ///     layers <c>DefaultPermissions</c> on, which carries <c>booking.*</c> and with it the
    ///     <c>*.view-all</c> bypass — so its caller sees every row whoever owns it — while
    ///     <c>CreateClientWithPermissions</c> withholds the bypass but invents the user id, which is
    ///     the very thing under test when ownership is the question. Measured: a first version of
    ///     the delegation case used <c>CreateClientAs</c> and could not have failed.
    /// </remarks>
    protected HttpClient CreateClientAsWithPermissions(
        string userId, string userName, params string[] permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", userId);
        client.DefaultRequestHeaders.Add("X-User-Name", userName);
        if (permissions.Length > 0)
            client.DefaultRequestHeaders.Add("X-User-Permissions", string.Join(",", permissions));
        return client;
    }

    /// <summary>
    ///     Creates a new HttpClient with specific permissions via X-User-Permissions header.
    ///     The user is authenticated (has X-User-Id) but only holds the given permissions.
    /// </summary>
    protected HttpClient CreateClientWithPermissions(params string[] permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", $"perm-user-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-User-Name", "Permission Test User");
        if (permissions.Length > 0)
            client.DefaultRequestHeaders.Add("X-User-Permissions", string.Join(",", permissions));
        return client;
    }

    /// <summary>
    ///     Creates a new HttpClient with specific roles via X-User-Roles header.
    ///     Permissions are resolved via role expansion (IRolePermissionStore).
    /// </summary>
    protected HttpClient CreateClientWithRoles(params string[] roles)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", $"role-user-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-User-Name", "Role Test User");
        if (roles.Length > 0)
            client.DefaultRequestHeaders.Add("X-User-Roles", string.Join(",", roles));
        return client;
    }

    /// <summary>
    ///     Creates a new HttpClient without any identity headers (anonymous user). It still carries a
    ///     tenant header — tenancy is orthogonal to authentication, and the app requires a tenant — so an
    ///     anonymous request reaches the auth layer and is rejected with 401 rather than short-circuiting
    ///     at tenant resolution (RequireTenant) with a 400.
    /// </summary>
    protected HttpClient CreateAnonymousClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        return client;
    }

    /// <summary>
    ///     Creates a new HttpClient that uses a JWT Bearer token for authentication.
    /// </summary>
    protected HttpClient CreateClientWithJwt(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected static async Task<HttpResponseMessage> PostWithClientAsync(HttpClient client, string url, object body)
    {
        return await client.PostAsJsonAsync(url, body, JsonOptions).ConfigureAwait(false);
    }

    /// <summary>
    ///     GET through a caller of the test's choosing — the read counterpart of
    ///     <see cref="PostWithClientAsync" />.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A test that writes through one client and reads through <see cref="Client" /> is reading as
    ///     a different user in a different tenant. Two of them did, and passed: they read a reservation
    ///     whose property belonged to another tenant, which worked only because the <c>DbSet</c> that
    ///     <c>[ReadAccess]</c> adds carried no tenant filter. It carries one now, so the read is a 404 —
    ///     which is the right answer, and this is the helper that asks the question the test meant.
    /// </remarks>
    protected static async Task<T> GetWithClientAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions).ConfigureAwait(false);
        return result!;
    }

    /// <summary>
    ///     POST that tolerates broken response bodies (e.g. circular references in JSON serialization).
    ///     Returns the status code; returns InternalServerError if the response stream is corrupted.
    /// </summary>
    protected async Task<HttpStatusCode> PostStatusOnlyAsync(string url, object body)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            };
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            return response.StatusCode;
        }
        catch (HttpRequestException)
        {
            // Server-side serialization error (e.g. circular references) corrupts the HTTP stream
            return HttpStatusCode.InternalServerError;
        }
        catch (IOException)
        {
            return HttpStatusCode.InternalServerError;
        }
    }

    /// <summary>
    ///     POST with a specific client that tolerates broken response bodies.
    /// </summary>
    protected static async Task<HttpStatusCode> PostStatusOnlyWithClientAsync(HttpClient client, string url, object body)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            return response.StatusCode;
        }
        catch (HttpRequestException)
        {
            return HttpStatusCode.InternalServerError;
        }
        catch (IOException)
        {
            return HttpStatusCode.InternalServerError;
        }
    }
}
