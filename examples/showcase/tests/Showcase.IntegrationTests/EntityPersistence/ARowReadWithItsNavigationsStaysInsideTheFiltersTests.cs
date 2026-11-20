using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;
using Showcase.Billing.Entities;
using Showcase.IntegrationTests.Endpoints;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     <c>IIncludingReadRepository.GetByIdIncludingAsync</c>, on every engine with navigations (#887): the row by its key with the navigations
///     its paths name, inside the caller's filters.
/// </summary>
/// <remarks>
///     <see cref="Invoice" /> is <c>[HasAccessScopes]</c>, and outside a request no caller holds a scope on it, so its row filter answers no
///     row. The pair is the case: read inside the filters the invoice is not there, and read with them lifted it is, with its lines. Without
///     the first a read that ignored the filters would pass; without the second, a read that answered nothing.
/// </remarks>
public class ARowReadWithItsNavigationsStaysInsideTheFiltersTests(PostgresFixture fixture) : InvoiceTestsBase(fixture)
{
    [Fact]
    public async Task InsideTheFilters_AnInvoiceNoCallerMaySee_IsNotRead()
    {
        var invoiceId = await AnInvoiceAsync();

        using var tenant = TenantScope.BeginScope("test-tenant");
        using var scope = Services.CreateScope();

        (await IncludingRead(scope).GetByIdIncludingAsync(invoiceId, nameof(Invoice.LineItems)))
            .Should().BeNull("the access scope filter applies to the read with navigations as to every read of the repository");
    }

    [Fact]
    public async Task WithTheFiltersLifted_TheInvoiceIsReadWithItsLines()
    {
        var invoiceId = await AnInvoiceAsync();

        using var tenant = TenantScope.BeginScope("test-tenant");
        using var scope = Services.CreateScope();
        using var filters = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>().DisableAll();

        var invoice = await IncludingRead(scope).GetByIdIncludingAsync(invoiceId, nameof(Invoice.LineItems));

        invoice.Should().NotBeNull("the filters are lifted, and the invoice is there");
        invoice!.LineItems.Should().NotBeEmpty("the path names the lines, and the read loads them");
    }

    /// <summary>The repository registered as <see cref="IReadRepository{TEntity}" />, cast, as the generated loads cast it.</summary>
    private static IIncludingReadRepository<Invoice> IncludingRead(IServiceScope scope)
        => (IIncludingReadRepository<Invoice>)scope.ServiceProvider.GetRequiredService<IReadRepository<Invoice>>();
}
