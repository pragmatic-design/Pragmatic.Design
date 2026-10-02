using System.Net;
using Conformance.Catalog.Entities;
using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A boundary enforces another's invariant <b>by injecting its read contract</b>, never the module —
///     <c>[Published]</c>.
/// </summary>
/// <remarks>
///     <para>
///         The generator emits the interface, the implementation and <c>Add{Module}Reads()</c>, and the
///         metadata next to them is what makes the host call the registration. Without it, an operation
///         injecting the contract would fail at resolution, <b>on the first request</b> — and a suite in
///         which nobody declares the attribute would have nothing to notice.
///     </para>
///     <para>
///         There is no hand-written line here: that <c>ICatalogReads</c> resolves is the assertion. The
///         contract is generated in the <b>Catalog</b> compilation, and Sales sees it because it references
///         Catalog — that is the direction, and it is what keeps the dependency acyclic.
///     </para>
///     <para>
///         The invariant is deliberately minimal: an order can be classified only with the name of a
///         category the Catalog knows. It pins the <em>shape</em>, not a domain.
///     </para>
/// </remarks>
public class TheContractAcrossTheBoundary(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<string> ACategoryNamedAsync(string name)
    {
        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var category = Category.Create();
        catalog.Set<Category>().Add(category);
        catalog.Entry(category).Property(nameof(Category.Name)).CurrentValue = name;
        await catalog.SaveChangesAsync();

        return name;
    }

    private async Task<Guid> AnOrderAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>The name the Catalog knows passes, and the write happens.</summary>
    [Fact]
    public async Task TheInvariant_IsEnforcedThroughTheContract()
    {
        var name = await ACategoryNamedAsync($"tools-{Guid.NewGuid():N}"[..16]);
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/classification", new { categoryName = name });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the category exists across the boundary, and the mutation read it through the contract");

        await using var scope = Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var order = await sales.Set<Order>().AsNoTracking().SingleAsync(o => o.PersistenceId == orderId);
        order.Notes.Should().Be(name, "the write happened after the check, not instead of it");
    }

    /// <summary>The control: a name the Catalog does not know is refused.</summary>
    /// <remarks>
    ///     Without this case, a read that always returned something — or an invariant never evaluated —
    ///     would leave the case above green.
    /// </remarks>
    [Fact]
    public async Task AnUnknownName_IsRefused()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/classification",
            new { categoryName = $"does-not-exist-{Guid.NewGuid():N}" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the invariant is the Catalog's, and Sales enforces it without knowing its table");

        // ⚠️ The status alone is not enough: without the route this case would be green with a routing
        // 404. The body tells «the operation refused» from «the operation is not there».
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Category",
            "the refusal names the other boundary's resource; a routing 404 has no body");

        await using var scope = Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var order = await sales.Set<Order>().AsNoTracking().SingleAsync(o => o.PersistenceId == orderId);
        order.Notes.Should().BeNull("a refusal does not write");
    }

    /// <summary>
    ///     And the second control: Sales has no other road to that table.
    /// </summary>
    /// <remarks>
    ///     <c>[ReadAccess&lt;CatalogItem&gt;]</c> puts a <c>DbSet</c> of the item in Sales' <c>DbContext</c>;
    ///     for <c>Category</c> there is no declaration, so Sales' model does not contain it. That is what
    ///     makes the contract the only road, instead of one road out of two.
    /// </remarks>
    [Fact]
    public async Task TheReadingBoundary_HasNoOtherWayToThatTable()
    {
        await using var scope = Services.CreateAsyncScope();

        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        catalog.Model.FindEntityType(typeof(Category)).Should().NotBeNull(
            "the table is the owner's");
        sales.Model.FindEntityType(typeof(Category)).Should().BeNull(
            "without [ReadAccess] Sales' model does not know the entity over there");
        sales.Model.FindEntityType(typeof(CatalogItem)).Should().NotBeNull(
            "and the comparison: where [ReadAccess] is declared, the DbSet is there");
    }
}
