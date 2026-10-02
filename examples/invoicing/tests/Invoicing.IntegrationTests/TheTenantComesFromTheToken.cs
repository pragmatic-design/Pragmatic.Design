using System.Net;
using System.Net.Http.Json;
using Invoicing.Billing.Entities;
using Invoicing.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     A company is a tenant: the tenant comes from the token, an unknown or suspended one is
///     not served, and a row belongs to the company that wrote it.
/// </summary>
/// <remarks>
///     <para>
///         There is no tenant header anywhere in this application or in these tests, and no custom
///         resolver: the claim is the only source. That is why the cross-tenant escalation the claim guard
///         exists for — an authenticated caller of one company supplying another's id — has no test here:
///         with claim-only resolution the resolved tenant <b>is</b> the claim, so the two can never differ.
///     </para>
///     <para>
///         The refusals below are the middleware's, at order 92 — before the route runs, and before
///         authorization. Their status codes are read from <c>TenantResolutionMiddleware</c>: 400 when no
///         tenant is resolved, 403 when the store says the tenant is not Active, 404 when the store does
///         not know it at all.
///     </para>
/// </remarks>
public sealed class TheTenantComesFromTheToken(PostgresFixture database) : InvoicingTestBase(database)
{
    [Fact]
    public async Task ATokenWithNoTenantClaim_IsRefusedWith400()
    {
        var response = await As(TestUsers.Accountant).GetAsync("api/me");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ATokenForAnUnknownTenant_IsRefusedWith404()
    {
        var response = await As(TestUsers.Accountant, tenant: "no-such-company").GetAsync("api/me");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unknown tenant is not a forbidden one, and 403 would confirm that the id names something");
    }

    /// <summary>
    ///     The control for the two above: the same call, from the same person, with a tenant that exists
    ///     and is active.
    /// </summary>
    [Fact]
    public async Task ATokenForAnOnboardedCompany_IsServed()
    {
        var tenant = await OnboardAsync();

        var response = await As(TestUsers.Accountant, tenant).GetAsync("api/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ASuspendedCompany_IsRefusedWith403_AndServedAgainAfterReactivation()
    {
        var tenant = await OnboardAsync();
        var administrator = As(TestUsers.PlatformAdministrator);
        var accountant = As(TestUsers.Accountant, tenant);

        await ReadSuccessAsync(await administrator.PostAsync($"api/organizations/{tenant}/suspend", null));
        var whileSuspended = await accountant.GetAsync("api/me");

        await ReadSuccessAsync(await administrator.PostAsync($"api/organizations/{tenant}/reactivate", null));
        var afterReactivation = await accountant.GetAsync("api/me");

        whileSuspended.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        afterReactivation.StatusCode.Should().Be(HttpStatusCode.OK,
            "suspending is not deleting: the same token works again");
    }

    /// <summary>
    ///     The control that <c>[TenantAgnostic]</c> is read on a <c>[DomainAction]</c> and not only on an
    ///     endpoint class: onboarding is called with a token that carries no tenant, and the company it
    ///     creates does not exist yet, so without the attribute this is 400 for ever.
    /// </summary>
    [Fact]
    public async Task Onboarding_WorksWithoutATenant()
    {
        var response = await As(TestUsers.PlatformAdministrator)
            .PostAsJsonAsync("api/organizations/onboard", new
            {
                slug = $"agnostic-{Guid.NewGuid():N}"[..20],
                legalName = "Agnostic S.r.l.",
                invoiceNumberPrefix = "AGN",
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "an operation that creates answers 201, and reaching it at all is what this measures");
    }

    [Fact]
    public async Task OnboardingTheSameSlugTwice_IsRefusedWith409()
    {
        var tenant = await OnboardAsync();

        var response = await As(TestUsers.PlatformAdministrator)
            .PostAsJsonAsync("api/organizations/onboard", new
            {
                slug = tenant,
                legalName = "Someone else",
                invoiceNumberPrefix = "OTH",
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    ///     A row belongs to the company that wrote it: written under one tenant, it is invisible to the
    ///     other and carries that tenant's id.
    /// </summary>
    /// <remarks>
    ///     Through the container rather than over HTTP, because the application has no operation that
    ///     writes a tenant row yet — the customers arrive with the next story, and their HTTP isolation
    ///     test with them. What is exercised here is the same machinery a request uses: the interceptor
    ///     that stamps <c>TenantId</c> on insert and the fail-closed filter that reads it, resolved from
    ///     the running host, with the tenant set the way a background job sets it.
    /// </remarks>
    [Fact]
    public async Task ARowWrittenUnderOneTenant_IsInvisibleToAnother()
    {
        var acme = await OnboardAsync("acme");
        var globex = await OnboardAsync("globex");

        await WriteAnInvoiceAsync(acme);

        var ofAcme = await InvoicesOfAsync(acme);
        var ofGlobex = await InvoicesOfAsync(globex);

        ofAcme.Should().HaveCount(1, "the row was written by this company");
        ofAcme[0].TenantId.Should().Be(acme, "the interceptor stamps the tenant that wrote it");
        ofGlobex.Should().BeEmpty("and the other company cannot see it");
    }

    private async Task WriteAnInvoiceAsync(string tenant)
    {
        using var tenantScope = TenantScope.BeginScope(tenant);
        await using var scope = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IRepository<Invoice>>().Add(Invoice.Create());

        // Keyed by boundary: each boundary's DbContext has its own unit of work, which is how two modules
        // on one database still commit through the right context.
        await scope.ServiceProvider
            .GetRequiredKeyedService<IUnitOfWork>(typeof(Invoicing.Billing.BillingBoundary))
            .SaveChangesAsync();
    }

    private async Task<List<Invoice>> InvoicesOfAsync(string tenant)
    {
        using var tenantScope = TenantScope.BeginScope(tenant);
        await using var scope = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        // Every invoice this tenant can see — which is the whole point being measured.
        return await scope.ServiceProvider.GetRequiredService<IReadRepository<Invoice>>()
            .FindAsync(Spec<Invoice>.Where(_ => true));
    }
}
