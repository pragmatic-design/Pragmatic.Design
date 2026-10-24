using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;
using PragmaticDocumentSamples;

// Pragmatic.Persistence.Documents by example: the same chains on MongoDB (PRAGMATIC_SAMPLES_MONGO,
// mongodb://user:password@host:port/?authSource=admin, a standalone server) and on Cosmos DB (PRAGMATIC_SAMPLES_COSMOS,
// AccountEndpoint=…;AccountKey=…;), each sample checking what it got against the seed. Exit code 0 when every check held: DOCS-SAMPLES-OK.
// Pragmatic.Persistence.Documents.Samples.Tests runs the same on both, in containers.

var total = new SampleRun(0, 0);
var ran = 0;

if (Environment.GetEnvironmentVariable("PRAGMATIC_SAMPLES_MONGO") is { } mongo)
{
    await using var client = new MongoClient(mongo);
    total += await DocumentSamples.RunMongoAsync(client);
    ran++;
}
else
{
    Console.WriteLine("MongoDB skipped: PRAGMATIC_SAMPLES_MONGO is not set.");
}

if (Environment.GetEnvironmentVariable("PRAGMATIC_SAMPLES_COSMOS") is { } cosmos)
{
    using var client = new CosmosClient(cosmos);
    total += await DocumentSamples.RunCosmosAsync(client);
    ran++;
}
else
{
    Console.WriteLine("Cosmos DB skipped: PRAGMATIC_SAMPLES_COSMOS is not set.");
}

Console.WriteLine(ran == 0 ? "DOCS-SAMPLES-NONE: set PRAGMATIC_SAMPLES_MONGO or PRAGMATIC_SAMPLES_COSMOS"
    : total.Failures == 0 ? $"DOCS-SAMPLES-OK: {total.Checks} checks" : $"DOCS-SAMPLES-FAILED: {total.Failures} of {total.Checks} checks");
return ran > 0 && total.Failures == 0 ? 0 : 1;
