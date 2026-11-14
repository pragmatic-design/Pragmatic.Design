using System.Net;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A <c>[NoTenant]</c> operation refuses a request that brings a tenant and runs with none for one that does not, in a host that
///     requires a tenant everywhere else (#724). Before it a platform operation could only be <c>[TenantAgnostic]</c>, which serves the
///     caller in the tenant it brings.
/// </summary>
public class APlatformOperationRunsInNoTenantTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Route = "/api/properties/platform-scope";

    [Fact]
    public async Task ARequestThatBringsATenant_IsRefused()
    {
        var response = await CreateClientAs("operator", "Operator").GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("TENANT_NOT_ALLOWED");
    }

    [Fact]
    public async Task ARequestWithoutATenant_RunsInNone()
    {
        var client = CreateClientAs("operator", "Operator");
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");

        var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("no tenant");
    }

    /// <summary>The control: a tenant-scoped route of the same host still refuses a request without a tenant.</summary>
    [Fact]
    public async Task ATenantScopedRouteWithoutATenant_IsStillRefused()
    {
        var client = CreateClientAs("operator", "Operator");
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");

        var response = await client.GetAsync("/api/properties/search");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("TENANT_REQUIRED");
    }
}
