using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Mongo.Bson;

// Native AOT smoke of the MongoDB driver: PRAGMATIC_AOT_MONGO names the server (mongodb://user:password@host:port/?authSource=admin),
// which publish-and-smoke-documents.ps1 starts. Handshake and SCRAM, writes, a find over several batches, decimal128 and guids read back,
// a duplicate key refused, a cursor killed early. Prints MONGO-AOT-OK and exits 0, or the differences and exits 1.

if (Environment.GetEnvironmentVariable("PRAGMATIC_AOT_MONGO") is not { } connectionString)
{
    Console.WriteLine("MongoDB not checked: PRAGMATIC_AOT_MONGO is not set.");
    return 1;
}

var failures = 0;
await using var client = new MongoClient(connectionString);
var database = "aot" + Guid.NewGuid().ToString("N")[..8];

var ping = await client.RunCommandAsync("admin", w => w.Write("ping", 1));
failures += Expect("handshake, SCRAM, ping", ping.TryGetNumber("ok", out var ok) && ok == 1);

var ids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
var inserted = await client.RunCommandAsync(database, w =>
{
    w.Write("insert", "items");
    w.StartArray("documents");
    for (var i = 0; i < ids.Count; i++)
    {
        w.StartDocument(i);
        w.Write("_id", ids[i]);
        w.Write("n", i);
        w.Write("price", i * 0.01m + 1000m);
        w.Write("at", new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).AddMinutes(i));
        w.EndDocument();
    }

    w.EndArray();
});
failures += Expect("insert", inserted.TryGetNumber("n", out var n) && n == 50);

var reply = await client.RunCommandAsync(database, w =>
{
    w.Write("find", "items");
    w.StartDocument("filter");
    w.StartDocument("n");
    w.Write("$gte", 10);
    w.EndDocument();
    w.EndDocument();
    w.StartDocument("sort");
    w.Write("n", 1);
    w.EndDocument();
    w.Write("batchSize", 7);
});
var read = new List<(Guid Id, int N, decimal Price, DateTimeOffset At)>();
await using (var cursor = client.Cursor(database, reply, batchSize: 7))
{
    await foreach (var document in cursor)
    {
        var reader = document.Reader();
        (Guid Id, int N, decimal Price, DateTimeOffset At) row = default;
        while (reader.ReadElement(out _, out var name))
        {
            if (name.SequenceEqual("_id"u8))
                row.Id = reader.ReadGuid();
            else if (name.SequenceEqual("n"u8))
                row.N = reader.ReadInt32();
            else if (name.SequenceEqual("price"u8))
                row.Price = reader.ReadDecimal();
            else if (name.SequenceEqual("at"u8))
                row.At = reader.ReadDateTime();
            else
                reader.Skip();
        }

        read.Add(row);
    }
}

failures += Expect("find over several batches", read.Count == 40 && read.Select(r => r.N).SequenceEqual(Enumerable.Range(10, 40))
    && read.All(r => r.Id == ids[r.N] && r.Price == r.N * 0.01m + 1000m && r.At == new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).AddMinutes(r.N)));

var duplicate = false;
try
{
    await client.RunCommandAsync(database, w =>
    {
        w.Write("insert", "items");
        w.StartArray("documents");
        w.StartDocument(0);
        w.Write("_id", ids[0]);
        w.EndDocument();
        w.EndArray();
    });
}
catch (MongoWriteException e) when (e.IsDuplicateKey)
{
    duplicate = true;
}

failures += Expect("duplicate key refused", duplicate);

var early = await client.RunCommandAsync(database, w =>
{
    w.Write("find", "items");
    w.Write("batchSize", 3);
});
var open = client.Cursor(database, early, batchSize: 3);
var wasOpen = open.Open;
await open.DisposeAsync();
failures += Expect("cursor killed early", wasOpen && !open.Open);

await client.RunCommandAsync(database, w => w.Write("dropDatabase", 1));
Console.WriteLine(failures == 0 ? "MONGO-AOT-OK" : $"MONGO-AOT-FAILED: {failures} differences");
return failures == 0 ? 0 : 1;

static int Expect(string what, bool condition)
{
    Console.WriteLine($"MongoDB {what}: {(condition ? "ok" : "FAIL")}");
    return condition ? 0 : 1;
}
