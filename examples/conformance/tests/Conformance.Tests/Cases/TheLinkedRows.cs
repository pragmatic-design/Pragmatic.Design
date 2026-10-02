using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     The rows chosen by <b>key</b>, without loading them — <c>[LinkIds]</c>.
/// </summary>
/// <remarks>
///     <para>
///         A collection of ids is not a collection of children: the rows already exist and nobody is writing
///         them, so sending back their whole shape to link them is a query per write that buys nothing. EF
///         needs the key and nothing more.
///     </para>
///     <para>
///         ⚠️ The case that decides whether the mechanism is real is the <b>third</b>: removing a label from
///         an order must not delete it. The framework removes the link, the relation decides the row — here
///         measured, not assumed.
///     </para>
/// </remarks>
public class TheLinkedRows(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnOrderAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        return created.GetProperty("id").GetGuid();
    }

    private async Task<Guid> ALabelAsync(string name)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        // The name has a private setter and is not a factory parameter: written through the entry, the
        // same road ReadAccessAcrossTheBoundary uses.
        var label = Label.Create();
        db.Add(label);
        db.Entry(label).Property(nameof(Label.Name)).CurrentValue = name;
        await db.SaveChangesAsync();

        return label.PersistenceId;
    }

    private async Task<List<Guid>> LabelsOfAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var order = await db.Set<Order>()
            .AsNoTracking()
            .Include(o => o.Labels)
            .SingleAsync(o => o.PersistenceId == orderId);

        return order.Labels.Select(l => l.PersistenceId).OrderBy(id => id).ToList();
    }

    /// <summary>
    ///     The ids choose the rows, and the row is not rewritten.
    /// </summary>
    [Fact]
    public async Task IdsChooseTheRows()
    {
        var orderId = await AnOrderAsync();
        var first = await ALabelAsync("urgent");
        var second = await ALabelAsync("fragile");

        var response = await PutAsync($"/api/orders/{orderId}/labels", new
        {
            reference = "ORD-LABELLED",
            labelIds = new[] { first, second },
        });
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);

        var linked = await LabelsOfAsync(orderId);

        linked.Should().BeEquivalentTo(new[] { first, second }.OrderBy(id => id).ToList());
    }

    /// <summary>
    ///     ⚠️ The set sent is the set that remains: <c>Sync</c> applies here as for children.
    /// </summary>
    [Fact]
    public async Task TheSetSentIsTheSetThatRemains()
    {
        var orderId = await AnOrderAsync();
        var first = await ALabelAsync("urgent");
        var second = await ALabelAsync("fragile");

        await PutAsync($"/api/orders/{orderId}/labels", new
        {
            reference = "ORD-BOTH",
            labelIds = new[] { first, second },
        });

        var response = await PutAsync($"/api/orders/{orderId}/labels", new
        {
            reference = "ORD-ONE",
            labelIds = new[] { second },
        });
        response.EnsureSuccessStatusCode();

        var linked = await LabelsOfAsync(orderId);

        linked.Should().BeEquivalentTo(new[] { second });
    }

    /// <summary>
    ///     ⚠️ The case that separates «removing a link» from «deleting a row».
    /// </summary>
    /// <remarks>
    ///     Without it, an implementation that deletes the unlinked row would pass the two cases above. It is
    ///     the difference between choosing and owning: a label exists on its own.
    /// </remarks>
    [Fact]
    public async Task UnlinkingARow_DoesNotDeleteIt()
    {
        var orderId = await AnOrderAsync();
        var label = await ALabelAsync("temporary");

        await PutAsync($"/api/orders/{orderId}/labels", new
        {
            reference = "ORD-LABELLED",
            labelIds = new[] { label },
        });

        await PutAsync($"/api/orders/{orderId}/labels", new
        {
            reference = "ORD-UNLABELLED",
            labelIds = Array.Empty<Guid>(),
        });

        (await LabelsOfAsync(orderId)).Should().BeEmpty();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        (await db.Set<Label>().AsNoTracking().AnyAsync(l => l.PersistenceId == label))
            .Should().BeTrue("removing the link is not deleting the row");
    }
}
