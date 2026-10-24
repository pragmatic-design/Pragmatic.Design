using Pragmatic.Persistence.Documents.Core.Tests;
using PragmaticDocumentSamples;
using Xunit;

namespace Pragmatic.Persistence.Documents.Samples.Tests;

/// <summary>
///     The documents samples, run as their program runs them, on each backend: every check holds, and the number of checks is the one the
///     samples make, so a sample that stopped checking is seen as well as one that fails.
/// </summary>
public sealed class DocumentSamplesRunTests(MongoServer mongo, CosmosServer cosmos) : IClassFixture<MongoServer>, IClassFixture<CosmosServer>
{
    // Any start that failed: the gate refuses to run this suite without Docker, so no failure here is Docker's absence.
    [Fact]
    public void The_servers_started()
    {
        Assert.Null(mongo.Unavailable);
        Assert.Null(cosmos.Unavailable);
    }

    [Fact]
    public async Task Every_sample_holds_on_MongoDB()
    {
        if (mongo.Unavailable is not null)
            return;
        var run = await DocumentSamples.RunMongoAsync(mongo.Client);
        Assert.Equal(new SampleRun(MongoChecks, 0), run);
    }

    [Fact]
    public async Task Every_sample_holds_on_Cosmos_DB()
    {
        if (cosmos.Unavailable is not null)
            return;
        var run = await DocumentSamples.RunCosmosAsync(cosmos.Client);
        Assert.Equal(new SampleRun(CosmosChecks, 0), run);
    }

    // The checks each backend's samples make: 82 in all. MongoDB runs the pipelines and the groups' HAVING; Cosmos DB the ordered page across
    // partitions instead.
    private const int MongoChecks = 42;

    private const int CosmosChecks = 40;
}
