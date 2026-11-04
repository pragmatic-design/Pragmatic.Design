using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.ReferencedModule;
using Pragmatic.Persistence.Repository;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     Documents of another module read on MongoDB and Cosmos DB (#502), as a boundary reads them through <c>[ReadAccess&lt;T&gt;]</c>: this
///     assembly sees <c>Part</c> and <c>Supplier</c> as metadata, where a private setter (<c>Code</c>, the generated <c>SupplierId</c>) has no
///     setter Roslyn can see. The module writes its documents; this assembly filters on the foreign key, reads a supplier's field into its own
///     order's card, and sees the suppliers the module's rule hides as missing.
/// </summary>
[Collection("document servers")]
public class ReferencedModuleTests(DocumentServers servers)
{
    private static async Task<DocSession> SessionAsync(DocumentServers servers, Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
        {
            await session.EnsureContainerAsync<Supplier>();
            await session.EnsureContainerAsync<Part>();
            await session.EnsureContainerAsync<PartOrder>();
        }

        return session;
    }

    private static string Prefix() => "R-" + Guid.NewGuid().ToString("N")[..10] + "-";

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Another_modules_document_is_filtered_on_its_foreign_key_and_read_whole(Store store)
    {
        var session = await SessionAsync(servers, store);
        var prefix = Prefix();
        var (acme, _, _) = await ReferencedDocuments.SeedAsync(session, prefix);

        var parts = await session.Query<Part>().Where((p, supplier) => p.SupplierId == supplier, acme).ToListAsync();

        Assert.Equal([prefix + "A-1", prefix + "A-2"], parts.Select(p => p.Code).Order());
        Assert.All(parts, p => Assert.Equal(acme, p.SupplierId));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Another_modules_field_is_read_into_a_card_and_a_supplier_its_rule_hides_reads_null(Store store)
    {
        var session = await SessionAsync(servers, store);
        var prefix = Prefix();
        var (acme, bolt, gone) = await ReferencedDocuments.SeedAsync(session, prefix);
        var orders = new[] { (1, acme), (2, bolt), (3, gone) }.Select(o =>
        {
            var order = new PartOrder { Reference = prefix + o.Item1, Quantity = o.Item1 };
            order.SetSupplierId(o.Item2);
            return order;
        }).ToList();
        await session.InsertManyAsync(orders);

        IReadRepository<PartOrder> repository = new PartOrder.DocReadRepository(session);
        var cards = await repository.RunAsync(new PartOrderCardsQuery { Reference = prefix });

        Assert.Equal([(1, prefix + "ACME"), (2, prefix + "BOLT"), (3, null)], cards.Select(c => (c.Quantity, c.SupplierName)));
    }
}
