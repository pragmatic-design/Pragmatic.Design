using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Invoicing.IntegrationTests.Infrastructure;
using Invoicing.Registry;
using Pragmatic.Identity.Authorization;
using Pragmatic.Endpoints.Authorization;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     The caller is whoever the company's OpenID Connect provider says, and what they may do is
///     what the roles in that token grant. Invoicing issues no token and has no password to lose.
/// </summary>
/// <remarks>
///     <para>
///         The tokens are signed by <see cref="TestIdentityProvider" /> and validated by the handler the
///         application registers: issuer, audience, lifetime and signature are checked for real. There is no
///         development identity middleware anywhere in this suite, and the host runs in <c>Testing</c>.
///     </para>
///     <para>
///         The access table has one operation because the application has one: the per-role differences
///         start with the story that adds the operations only some roles may call. The two table tests are
///         the ratchet that makes that growth visible — an endpoint added and left out of the table, or one
///         that asks for nothing but a token, fails here.
///     </para>
/// </remarks>
public sealed class SigningInWithTheProvider(PostgresFixture database) : InvoicingTestBase(database)
{
    private const string Viewer = nameof(Viewer);
    private const string Accountant = nameof(Accountant);
    private const string Administrator = nameof(Administrator);

    private static readonly string[] Everyone = [Viewer, Accountant, Administrator];

    /// <summary>Whoever keeps the register: the accountant, and the administrator who includes that role.</summary>
    private static readonly string[] Accountants = [Accountant, Administrator];

    /// <summary>Every operation that needs a signed-in caller, and the roles that may perform it.</summary>
    /// <summary>
    ///     How many routes this application maps — the table's own size, and the table is asserted
    ///     against what ASP.NET mapped by <see cref="EveryEndpoint_IsInTheTable_AndDeclaresWhoMayCallIt" />.
    /// </summary>
    /// <remarks>
    ///     Exposed so the README's count can be checked against it rather than remembered
    ///     (<see cref="TheReadmeQuotesWhatTheSuiteMeasures" />).
    /// </remarks>
    internal static int RouteCount => Operations.Length;

    private static readonly Operation[] Operations =
    [
        new("GET", "api/me", Everyone),
        new("POST", "api/organizations/onboard", [Administrator]),
        new("POST", "api/organizations/{slug}/suspend", [Administrator]),
        new("POST", "api/organizations/{slug}/reactivate", [Administrator]),
        new("POST", "api/customers", Accountants),
        new("PUT", "api/customers/{id}", Accountants),
        new("DELETE", "api/customers/{id}", Accountants),
        new("POST", "api/customers/{id}/restore", Accountants),
        new("GET", "api/customers", Everyone),
        new("GET", "api/customers/{id}", Everyone),
        new("GET", "api/invoices", Everyone),
        new("GET", "api/invoices/{id}", Everyone),
        new("GET", "api/invoices/outstanding", Everyone),
        new("POST", "api/invoices", Accountants),
        new("PUT", "api/invoices/{id}", Accountants),
        new("DELETE", "api/invoices/{id}", Accountants),
        new("POST", "api/invoices/{id}/issue", Accountants),
        new("POST", "api/invoices/{id}/void", Accountants),
        new("GET", "api/invoices/{id}/pdf", Accountants),
        new("POST", "api/invoices/{id}/payments", Accountants),
        new("DELETE", "api/invoices/{invoiceId}/payments/{id}", Accountants),
        new("GET", "api/invoices/{id}/payments", Everyone)
    ];

    /// <summary>
    ///     The routes that belong to no tenant, and are therefore the only ones an anonymous caller can be
    ///     refused by <em>authentication</em>: everywhere else the tenant middleware speaks first.
    /// </summary>
    private static readonly Operation[] TenantAgnostic =
        [.. Operations.Where(o => o.Route.StartsWith("api/organizations", StringComparison.Ordinal))];

    /// <summary>
    ///     On a tenant-bound route an anonymous caller is refused with **400**, not 401: tenant resolution
    ///     runs at order 92 and authorization at 93, and a request with no token resolves no tenant. The
    ///     401 is asserted below, where it can be — on the routes that need no tenant.
    /// </summary>
    [Fact]
    public async Task AnAnonymousRequest_ToATenantRoute_IsRefusedByTheTenantFirst()
    {
        var response = await Client.GetAsync("api/me");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnAnonymousRequest_ToATenantAgnosticRoute_IsRefusedWith401()
    {
        var wrong = new List<string>();
        foreach (var operation in TenantAgnostic)
        {
            var status = (await Client.SendAsync(operation.Request())).StatusCode;
            if (status != HttpStatusCode.Unauthorized)
                wrong.Add($"anonymous {operation}: {(int)status}");
        }

        wrong.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenFromTheProvider_NamesTheCaller()
    {
        var caller = await ReadJsonAsync(await As(TestUsers.Accountant, await OnboardAsync()).GetAsync("api/me"));

        caller.GetProperty("id").GetString().Should().Be(TestUsers.Accountant.Id);
        caller.GetProperty("displayName").GetString().Should().Be(TestUsers.Accountant.Name);
    }

    /// <summary>
    ///     The roles arrive as one claim holding a JSON array — the shape a provider sends — and come out the
    ///     other end as the permissions the role's class grants.
    /// </summary>
    [Fact]
    public async Task TheRolesInTheToken_BecomeThePermissionsOfTheirRole()
    {
        var caller = await ReadJsonAsync(await As(TestUsers.Viewer, await OnboardAsync()).GetAsync("api/me"));

        var roles = caller.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        var permissions = caller.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();

        roles.Should().Contain("viewer");
        permissions.Should().Contain(RegistryPermissions.Organization.Read);
        permissions.Should().NotContain(RegistryPermissions.Organization.Onboard,
            "a viewer is not a platform administrator");
    }

    /// <summary>
    ///     The test that would have caught an unset <c>Audience</c>: same issuer, same signature, a token
    ///     minted for another client of the same provider.
    /// </summary>
    /// <summary>
    ///     The test that would have caught an unset <c>Audience</c>: same issuer, same signature, a token
    ///     minted for another client of the same provider.
    /// </summary>
    /// <remarks>
    ///     Asked of a tenant-agnostic route on purpose. A rejected token leaves the caller anonymous, and an
    ///     anonymous caller resolves no tenant — so on any other route the 401 would be hidden behind the
    ///     tenant middleware's 400, and the test would pass for a reason that has nothing to do with the
    ///     audience.
    /// </remarks>
    [Fact]
    public async Task ATokenMintedForAnotherAudience_IsRefused()
    {
        var response = await As(TestUsers.PlatformAdministrator, audience: "some-other-client")
            .PostAsync("api/organizations/onboard", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenWithNoRole_IsRefusedWith403()
    {
        var tenant = await OnboardAsync();

        var response = await As(TestUsers.WithoutRole, tenant).GetAsync("api/me");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EachRole_ReachesItsOperations_AndIsRefusedTheOthers()
    {
        var tenant = await OnboardAsync();
        var clients = new Dictionary<string, HttpClient>
        {
            [Viewer] = As(TestUsers.Viewer, tenant),
            [Accountant] = As(TestUsers.Accountant, tenant),
            [Administrator] = As(TestUsers.PlatformAdministrator, tenant)
        };

        var wrong = new List<string>();
        foreach (var operation in Operations)
        {
            foreach (var (role, client) in clients)
            {
                var status = (await client.SendAsync(operation.Request())).StatusCode;
                var allowed = operation.Roles.Contains(role);
                if (allowed && status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    wrong.Add($"{role} refused {operation}: {(int)status}");
                if (!allowed && status != HttpStatusCode.Forbidden)
                    wrong.Add($"{role} not refused {operation}: {(int)status}");
            }
        }

        wrong.Should().BeEmpty();
    }

    /// <summary>
    ///     The table is every endpoint the application maps, and every one of them declares who may call it:
    ///     an endpoint added without a permission, or left out of the table, fails here.
    /// </summary>
    [Fact]
    public void EveryEndpoint_IsInTheTable_AndDeclaresWhoMayCallIt()
    {
        var mapped = Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Route: e.RoutePattern.RawText?.TrimStart('/') ?? "", Endpoint: e))
            .Where(e => e.Route.StartsWith("api/", StringComparison.Ordinal))
            .SelectMany(e => (e.Endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (Key: $"{method} {e.Route}", e.Endpoint)))
            .ToList();

        mapped.Select(m => m.Key).Should().BeEquivalentTo(Operations.Select(o => o.ToString()));

        // A permission, not merely a signed-in caller: an endpoint that asks for nothing more than a token
        // is open to every role there is, and to every role added later.
        var undeclared = mapped
            .Where(m => m.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
                        && !m.Endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>()
                            .SelectMany(p => p.Requirements)
                            .OfType<PragmaticPermissionRequirement>()
                            .Any())
            .Select(m => m.Key);

        undeclared.Should().BeEmpty("every operation declares the permission it needs, or that it needs none");
    }

    private sealed record Operation(string Method, string Route, string[] Roles)
    {
        /// <summary>
        ///     The call itself. Every route parameter but the slug is filled with an empty id — a route with
        ///     two of them is why they are not replaced by name. A value that names nothing is fine:
        ///     authorization answers before the handler does, so a 404 from the operation still means the
        ///     caller reached it.
        /// </summary>
        public HttpRequestMessage Request() =>
            new(new HttpMethod(Method), System.Text.RegularExpressions.Regex.Replace(
                Route.Replace("{slug}", "no-such-company", StringComparison.Ordinal),
                @"\{\w+\}",
                Guid.Empty.ToString()));

        public override string ToString() => $"{Method} {Route}";
    }
}
