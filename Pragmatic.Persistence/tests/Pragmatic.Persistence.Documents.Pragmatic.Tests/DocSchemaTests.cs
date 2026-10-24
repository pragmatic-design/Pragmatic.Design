using System.Net;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The schema the documents engine describes for Pragmatic's entities (<c>DefaultDocSchema</c>), applied by <c>DocSchema.EnsureAsync</c> on
///     both stores: created, then found unchanged; the unique indexes holding; what Cosmos DB cannot apply reported with its reason; a changed
///     declaration applied where the store allows it and reported where it does not.
/// </summary>
[Collection("document servers")]
public class DocSchemaTests(DocumentServers servers)
{
    private static DocCollectionSchema Collection(string name) => DefaultDocSchema.Collections.Single(c => c.Collection == name);

    [Fact]
    public void The_schema_declares_the_entities_indexes_partition_and_composites()
    {
        var tags = Collection("SchemaTags");
        Assert.Equal("/id", tags.PartitionKeyPath);
        Assert.Contains(new DocIndex("IX_SchemaTags_Code", ["Code"], Unique: true, LiveField: "IsDeleted"), tags.Indexes, IndexComparer.Instance);
        var products = Collection("SchemaProducts");
        Assert.Equal("/TenantId", products.PartitionKeyPath);
        Assert.Contains(new DocIndex("IX_SchemaProducts_TenantId_Sku", ["TenantId", "Sku"], Unique: true), products.Indexes, IndexComparer.Instance);
        Assert.Equal([("/Name", false), ("/Rank", true)], Assert.Single(products.Composites));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_schema_is_created_then_found_unchanged(Store store)
    {
        var session = servers.Session(store);
        var created = await DefaultDocSchema.EnsureAsync(session);
        Assert.False(created.Unchanged);
        Assert.Contains(created.Changes, c => c.Outcome == DocSchemaOutcome.Created);

        var again = await DefaultDocSchema.EnsureAsync(session);
        Assert.True(again.Unchanged, string.Join("; ", again.Changes.Where(c => c.Outcome is DocSchemaOutcome.Created or DocSchemaOutcome.Updated).Select(c => $"{c.Collection} {c.Element} {c.Outcome}")));
        Assert.Equal(created.NotApplied.Select(c => (c.Collection, c.Element)), again.NotApplied.Select(c => (c.Collection, c.Element))); // reported every time, not once
    }

    [Fact]
    public async Task On_MongoDB_a_live_duplicate_is_refused_and_a_deleted_one_frees_its_code()
    {
        var session = servers.Session(Store.Mongo);
        await DefaultDocSchema.EnsureAsync(session);
        await session.InsertAsync(new SchemaTag { Code = "blue" });
        var duplicate = await Assert.ThrowsAsync<MongoWriteException>(() => session.InsertAsync(new SchemaTag { Code = "blue" }));
        Assert.True(duplicate.IsDuplicateKey);

        await session.InsertAsync(new SchemaTag { Code = "red", IsDeleted = true });
        await session.InsertAsync(new SchemaTag { Code = "red" }); // the index holds the live tags only
    }

    [Fact]
    public async Task On_Cosmos_DB_a_tenants_duplicate_is_refused_and_what_it_cannot_hold_is_reported()
    {
        var session = servers.Session(Store.Cosmos);
        var report = await DefaultDocSchema.EnsureAsync(session);
        var live = Assert.Single(report.NotApplied, c => c.Element == "IX_SchemaTags_Code");
        Assert.Contains("live documents", live.Reason);

        await session.InsertAsync(new SchemaProduct { Sku = "A-1", Name = "Lamp", TenantId = "acme" });
        var duplicate = await Assert.ThrowsAsync<CosmosException>(() => session.InsertAsync(new SchemaProduct { Sku = "A-1", Name = "Lamp", TenantId = "acme" }));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.Status);
        await session.InsertAsync(new SchemaProduct { Sku = "A-1", Name = "Lamp", TenantId = "globex" }); // another tenant's partition
    }

    [Fact]
    public async Task On_Cosmos_DB_a_changed_unique_key_is_reported_and_a_changed_composite_applied()
    {
        var session = servers.Session(Store.Cosmos);
        await DefaultDocSchema.EnsureAsync(session);
        var products = Collection("SchemaProducts");
        var changed = products with
        {
            Indexes = [.. products.Indexes, new DocIndex("IX_SchemaProducts_TenantId_Name", ["TenantId", "Name"], Unique: true)],
            Composites = [new (string, bool)[] { ("/Rank", false), ("/Name", true) }],
        };

        var report = await DocSchema.EnsureAsync(session, [changed]);
        var unique = Assert.Single(report.Changes, c => c.Element == "unique keys");
        Assert.Equal(DocSchemaOutcome.NotApplied, unique.Outcome);
        Assert.Contains("fixes them at creation", unique.Reason);
        Assert.Equal(DocSchemaOutcome.Updated, Assert.Single(report.Changes, c => c.Element == "composite indexes").Outcome);

        var again = await DocSchema.EnsureAsync(session, [changed]);
        Assert.Equal(DocSchemaOutcome.Unchanged, Assert.Single(again.Changes, c => c.Element == "composite indexes").Outcome);
    }

    [Fact]
    public async Task On_MongoDB_a_changed_index_is_built_again()
    {
        var session = servers.Session(Store.Mongo);
        await DefaultDocSchema.EnsureAsync(session);
        var products = Collection("SchemaProducts");
        var changed = products with { Indexes = [.. products.Indexes.Select(i => i.Name == "IX_SchemaProducts_TenantId" ? i with { Unique = true } : i)] };

        Assert.Equal(DocSchemaOutcome.Updated, Assert.Single((await DocSchema.EnsureAsync(session, [changed])).Changes, c => c.Element == "IX_SchemaProducts_TenantId").Outcome);
        Assert.True((await DocSchema.EnsureAsync(session, [changed])).Unchanged);
    }

    /// <summary>A DocIndex by value: a record compares its field list by reference.</summary>
    private sealed class IndexComparer : IEqualityComparer<DocIndex>
    {
        public static readonly IndexComparer Instance = new();

        public bool Equals(DocIndex? x, DocIndex? y)
            => x is not null && y is not null && x.Name == y.Name && x.Fields.SequenceEqual(y.Fields) && x.Unique == y.Unique && x.LiveField == y.LiveField;

        public int GetHashCode(DocIndex obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }
}
