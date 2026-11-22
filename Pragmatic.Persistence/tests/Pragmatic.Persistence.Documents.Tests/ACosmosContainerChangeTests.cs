using Pragmatic.Persistence.Documents;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A changed declaration applied to a Cosmos DB container that already exists: the unique keys reported, since Cosmos DB fixes them at
///     creation, and the composite indexes replaced.
/// </summary>
/// <remarks>
///     On an emulator of its own (#951). Replacing a container's indexing policy on the vNext emulator answered
///     <c>500 Failed to update collection: PostgresError(SqlState(E58P01))</c> after the rest of the suite had run on the same emulator, four
///     full runs out of five on one machine, and never when this test ran alone or after the same tests in the same order. E58P01 is
///     PostgreSQL's <c>undefined_file</c>, inside the emulator's own store: the state that breaks it is the emulator's, accumulated by the
///     suite, not anything a test leaves behind. A fresh emulator for this one case keeps the assertion and drops that state.
/// </remarks>
[Collection("a cosmos container changed")]
public class ACosmosContainerChangeTests(DocumentServers servers)
{
    [Fact]
    public async Task On_Cosmos_DB_a_changed_unique_key_is_reported_and_a_changed_composite_applied()
    {
        var session = servers.Session(Store.Cosmos);
        await DefaultDocSchema.EnsureAsync(session);
        var products = DefaultDocSchema.Collections.Single(c => c.Collection == "SchemaProducts");
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
}
