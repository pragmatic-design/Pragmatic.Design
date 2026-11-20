using System.Net;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result.Database;
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
        Assert.Contains(new DocIndex("IX_SchemaProducts_Name_Rank", ["Name", "Rank"]), products.Indexes, IndexComparer.Instance); // [Index]: not unique, no tenant
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

    /// <summary>
    ///     A <c>[LogicKey]</c> is a unique index, read by the rule the Sql engine and EF Core read it with (#607): the tenant leading, then the
    ///     key's parts.
    /// </summary>
    [Fact]
    public void A_logic_key_is_declared_unique_within_the_tenant()
        => Assert.Contains(new DocIndex("IX_SchemaBrands_TenantId_Code", ["TenantId", "Code"], Unique: true), Collection("SchemaBrands").Indexes, IndexComparer.Instance);

    /// <summary>A second brand with the same code in the same tenant is refused; the control, another code, is written.</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_duplicate_logic_key_is_refused(Store store)
    {
        var session = servers.Session(store);
        await DefaultDocSchema.EnsureAsync(session);
        var code = "B-" + Guid.NewGuid().ToString("N")[..8];
        await session.InsertAsync(new SchemaBrand { Code = code, TenantId = "acme" });

        var duplicate = await Record.ExceptionAsync(() => session.InsertAsync(new SchemaBrand { Code = code, TenantId = "acme" }));

        Assert.True(duplicate is MongoWriteException { IsDuplicateKey: true } or CosmosException { Status: HttpStatusCode.Conflict }, duplicate?.ToString() ?? "no exception");
        await session.InsertAsync(new SchemaBrand { Code = code + "-other", TenantId = "acme" });
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

    /// <summary>
    ///     A temporal relation with <c>MaxActive = 1</c> declares, as the Sql engine's schema does (#900): one open stretch per parent, unique on the
    ///     parent's key among the documents whose ValidTo is null, and the range, ValidTo null or not before ValidFrom.
    /// </summary>
    [Fact]
    public void A_temporal_relation_declares_its_open_stretch_and_its_range()
    {
        var duties = Collection("DeskDuties");
        Assert.Contains(new DocIndex("IX_DeskDuties_PropertyId_Active", ["PropertyId"], Unique: true, NullField: "ValidTo"), duties.Indexes, IndexComparer.Instance);
        Assert.Equal(new DocRange("CK_DeskDuties_ValidRange", "ValidFrom", "ValidTo"), Assert.Single(duties.Ranges!));
    }

    /// <summary>
    ///     Written by the session, past any mutation's check: a second open stretch at the same property and a stretch ending before it begins
    ///     are refused by MongoDB; a second stretch that is closed, and one at another property, are written.
    /// </summary>
    [Fact]
    public async Task On_MongoDB_a_second_open_stretch_and_an_inverted_one_are_refused()
    {
        var session = servers.Session(Store.Mongo);
        var report = await DefaultDocSchema.EnsureAsync(session);
        Assert.DoesNotContain(report.NotApplied, c => c.Collection == "DeskDuties");
        var riva = Guid.NewGuid();
        var t = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
        await session.InsertAsync(Duty(riva, "anna", t));

        var second = await Assert.ThrowsAsync<MongoWriteException>(() => session.InsertAsync(Duty(riva, "bruno", t.AddHours(1))));
        Assert.True(second.IsDuplicateKey);
        var inverted = await Assert.ThrowsAsync<MongoWriteException>(() => session.InsertAsync(Duty(riva, "carla", t, t.AddHours(-1))));
        Assert.Equal(121, inverted.Code); // DocumentValidationFailure

        await session.InsertAsync(Duty(riva, "dario", t.AddDays(-2), t.AddDays(-1))); // closed
        await session.InsertAsync(Duty(Guid.NewGuid(), "bruno", t)); // another property
    }

    private static DeskDuty Duty(Guid propertyId, string person, DateTimeOffset from, DateTimeOffset? to = null)
    {
        var duty = new DeskDuty { Person = person, ValidFrom = from, ValidTo = to };
        duty.SetPropertyId(propertyId);
        return duty;
    }

    /// <summary>
    ///     The same refusals through the unit of work are broken rules, as on the Sql engine: the open stretch a conflict, the range a constraint
    ///     error, never the store's raw exception.
    /// </summary>
    [Fact]
    public async Task On_MongoDB_the_unit_of_work_reports_the_refusals_as_broken_rules()
    {
        var session = servers.Session(Store.Mongo);
        await DefaultDocSchema.EnsureAsync(session);
        var riva = Guid.NewGuid();
        var t = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
        await session.InsertAsync(Duty(riva, "anna", t));

        async Task<PersistenceRuleViolationException> SaveAsync(DeskDuty duty)
        {
            var unitOfWork = new DocUnitOfWork(session);
            new DeskDuty.DocRepository(unitOfWork).Add(duty);
            return await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => unitOfWork.SaveChangesAsync());
        }

        Assert.IsType<DbConflictError>((await SaveAsync(Duty(riva, "bruno", t.AddHours(1)))).Error);
        Assert.Equal("Check", Assert.IsType<DbConstraintError>((await SaveAsync(Duty(Guid.NewGuid(), "carla", t, t.AddHours(-1)))).Error).ConstraintType);
    }

    /// <summary>
    ///     Cosmos DB holds neither: both are reported as the mutation's check only, with their reason, and not as a failure, so a Pragmatic host
    ///     on Cosmos DB still starts (its start stops on <see cref="DocSchemaReport.NotApplied" />), as the Sql engine reports PRAG3039.
    /// </summary>
    [Fact]
    public async Task On_Cosmos_DB_the_open_stretch_and_the_range_are_reported_as_the_mutations_check()
    {
        var report = await DefaultDocSchema.EnsureAsync(servers.Session(Store.Cosmos));

        Assert.Contains("open stretch", Assert.Single(report.MutationOnly, c => c.Element == "IX_DeskDuties_PropertyId_Active").Reason);
        Assert.Contains("validates no document's content", Assert.Single(report.MutationOnly, c => c.Element == "CK_DeskDuties_ValidRange").Reason);
        Assert.DoesNotContain(report.NotApplied, c => c.Collection == "DeskDuties");
    }

    /// <summary>A DocIndex by value: a record compares its field list by reference.</summary>
    private sealed class IndexComparer : IEqualityComparer<DocIndex>
    {
        public static readonly IndexComparer Instance = new();

        public bool Equals(DocIndex? x, DocIndex? y)
            => x is not null && y is not null && x.Name == y.Name && x.Fields.SequenceEqual(y.Fields) && x.Unique == y.Unique && x.LiveField == y.LiveField
               && x.NullField == y.NullField;

        public int GetHashCode(DocIndex obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }
}
