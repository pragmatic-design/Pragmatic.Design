using System.Globalization;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Mongo.Bson;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     The driver against MongoDB 8: handshake and SCRAM-SHA-256, a refused password, writes, a find read over several batches, a duplicate key,
///     a cursor closed early, and decimal128 checked by the server itself (<c>$toString</c> of what the driver wrote, <c>$toDecimal</c> read back).
/// </summary>
[Collection("mongo")]
public class MongoDriverTests(MongoServer mongo)
{
    // Any start that failed: the gate refuses to run this suite without Docker, so no failure here is Docker's absence.
    [Fact]
    public void The_server_started()
        => Assert.Null(mongo.Unavailable);

    [Fact]
    public async Task A_ping_runs_after_the_handshake_and_scram()
    {
        if (mongo.Unavailable is not null)
            return;
        var reply = await mongo.Client.RunCommandAsync("admin", w => w.Write("ping", 1));
        Assert.True(reply.TryGetNumber("ok", out var ok) && ok == 1);
        var status = await mongo.Client.RunCommandAsync("admin", w => w.Write("connectionStatus", 1));
        Assert.True(status.TryGetDocument("authInfo", out var auth) && auth.TryGetDocument("authenticatedUsers", out var users) && users.Documents().Count == 1);
    }

    // A client of its own: its first command opens the connection, so the handshake and SCRAM run on the synchronous calls too.
    [Fact]
    public async Task A_synchronous_command_opens_authenticates_and_answers()
    {
        if (mongo.Unavailable is not null)
            return;
        await using var client = new MongoClient(mongo.ConnectionString);
        var reply = client.RunCommand("admin", w => w.Write("ping", 1));
        Assert.True(reply.TryGetNumber("ok", out var ok) && ok == 1);
        var status = client.RunCommand("admin", w => w.Write("connectionStatus", 1));
        Assert.True(status.TryGetDocument("authInfo", out var auth) && auth.TryGetDocument("authenticatedUsers", out var users) && users.Documents().Count == 1);
        var settings = MongoSettings.Parse(mongo.ConnectionString) with { Password = "not-the-password" };
        await using var refusing = new MongoClient(settings);
        Assert.Throws<MongoAuthenticationException>(() => refusing.RunCommand("admin", w => w.Write("ping", 1)));
    }

    [Fact]
    public async Task A_wrong_password_is_refused()
    {
        if (mongo.Unavailable is not null)
            return;
        var settings = MongoSettings.Parse(mongo.ConnectionString) with { Password = "not-the-password" };
        await using var client = new MongoClient(settings);
        var refused = await Assert.ThrowsAsync<MongoAuthenticationException>(() => client.RunCommandAsync("admin", w => w.Write("ping", 1)));
        Assert.Contains("SCRAM-SHA-256", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Documents_written_are_found_sorted_over_several_batches()
    {
        if (mongo.Unavailable is not null)
            return;
        var database = MongoServer.Database();
        var ids = Enumerable.Range(0, 35).Select(_ => Guid.NewGuid()).ToList();
        var inserted = await mongo.Client.RunCommandAsync(database, w =>
        {
            w.Write("insert", "items");
            w.StartArray("documents");
            for (var i = 0; i < ids.Count; i++)
            {
                w.StartDocument(i);
                w.Write("_id", ids[i]);
                w.Write("n", i);
                w.Write("price", i * 1.25m);
                w.Write("even", i % 2 == 0);
                w.EndDocument();
            }

            w.EndArray();
        });
        Assert.True(inserted.TryGetNumber("n", out var n) && n == 35);

        // Even ones, by n descending, in batches of 4: the first batch, then getMore until the server closes the cursor.
        var reply = await mongo.Client.RunCommandAsync(database, w =>
        {
            w.Write("find", "items");
            w.StartDocument("filter");
            w.Write("even", true);
            w.EndDocument();
            w.StartDocument("sort");
            w.Write("n", -1);
            w.EndDocument();
            w.Write("batchSize", 4);
        });
        await using var cursor = mongo.Client.Cursor(database, reply, batchSize: 4);
        var found = new List<(Guid Id, int N, decimal Price)>();
        await foreach (var document in cursor)
        {
            var reader = document.Reader();
            Guid id = default;
            int number = 0;
            decimal price = 0;
            while (reader.ReadElement(out _, out var name))
            {
                if (name.SequenceEqual("_id"u8))
                    id = reader.ReadGuid();
                else if (name.SequenceEqual("n"u8))
                    number = reader.ReadInt32();
                else if (name.SequenceEqual("price"u8))
                    price = reader.ReadDecimal();
                else
                    reader.Skip();
            }

            found.Add((id, number, price));
        }

        var expected = Enumerable.Range(0, 35).Where(i => i % 2 == 0).OrderByDescending(i => i).ToList();
        Assert.Equal(expected, found.Select(f => f.N));
        Assert.Equal(expected.Select(i => ids[i]), found.Select(f => f.Id));
        Assert.Equal(expected.Select(i => i * 1.25m), found.Select(f => f.Price));
        Assert.False(cursor.Open);
    }

    [Fact]
    public async Task A_duplicate_key_is_a_write_error_with_its_code()
    {
        if (mongo.Unavailable is not null)
            return;
        var database = MongoServer.Database();
        var id = Guid.NewGuid();
        await mongo.Client.RunCommandAsync(database, w => Insert(w, id));
        var refused = await Assert.ThrowsAsync<MongoWriteException>(() => mongo.Client.RunCommandAsync(database, w => Insert(w, id)));
        Assert.True(refused.IsDuplicateKey, refused.Message);
        // The connection stays usable after a refused write.
        var count = await mongo.Client.RunCommandAsync(database, w => w.Write("count", "items"));
        Assert.True(count.TryGetNumber("n", out var n) && n == 1);
    }

    [Fact]
    public async Task A_cursor_closed_early_is_killed_on_the_server()
    {
        if (mongo.Unavailable is not null)
            return;
        var database = MongoServer.Database();
        await mongo.Client.RunCommandAsync(database, w =>
        {
            w.Write("insert", "items");
            w.StartArray("documents");
            for (var i = 0; i < 10; i++)
            {
                w.StartDocument(i);
                w.Write("n", i);
                w.EndDocument();
            }

            w.EndArray();
        });
        var reply = await mongo.Client.RunCommandAsync(database, w =>
        {
            w.Write("find", "items");
            w.Write("batchSize", 2);
        });
        var cursor = mongo.Client.Cursor(database, reply, batchSize: 2);
        Assert.True(cursor.Open);
        await cursor.DisposeAsync();
        Assert.False(cursor.Open);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-1.5")]
    [InlineData("1234.5678")]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335")]
    [InlineData("7.9228162514264337593543950335")]
    [InlineData("1.10")]
    public async Task Decimal128_written_is_the_number_the_server_reads(string text)
    {
        if (mongo.Unavailable is not null)
            return;
        var value = decimal.Parse(text, CultureInfo.InvariantCulture);
        var database = MongoServer.Database();
        await mongo.Client.RunCommandAsync(database, w =>
        {
            w.Write("insert", "numbers");
            w.StartArray("documents");
            w.StartDocument(0);
            w.Write("_id", 1);
            w.Write("value", value);
            w.EndDocument();
            w.EndArray();
        });
        // The server's own rendering of the stored decimal128, and the server's decimal128 of the text, read back by the driver.
        var reply = await mongo.Client.RunCommandAsync(database, w =>
        {
            w.Write("aggregate", "numbers");
            w.StartArray("pipeline");
            w.StartDocument(0);
            w.StartDocument("$project");
            w.StartDocument("text");
            w.Write("$toString", "$value");
            w.EndDocument();
            w.StartDocument("parsed");
            w.Write("$toDecimal", text);
            w.EndDocument();
            w.EndDocument();
            w.EndDocument();
            w.EndArray();
            w.StartDocument("cursor");
            w.EndDocument();
        });
        var row = (await mongo.Client.Cursor(database, reply).ToListAsync()).Single();
        Assert.True(row.TryGetString("text", out var rendered));
        Assert.Equal(value, decimal.Parse(rendered, NumberStyles.Float, CultureInfo.InvariantCulture)); // the same number (the server may write 1E+3 forms)
        Assert.Equal(text, rendered.Contains('E', StringComparison.Ordinal) ? text : rendered); // and the same digits, trailing zeros kept, when it writes them plainly
        var reader = row.Reader();
        decimal parsed = 0;
        while (reader.ReadElement(out var type, out var name))
        {
            if (name.SequenceEqual("parsed"u8) && type == BsonType.Decimal128)
                parsed = reader.ReadDecimal();
            else
                reader.Skip();
        }

        Assert.Equal(value, parsed);
    }

    private static void Insert(BsonWriter w, Guid id)
    {
        w.Write("insert", "items");
        w.StartArray("documents");
        w.StartDocument(0);
        w.Write("_id", id);
        w.EndDocument();
        w.EndArray();
    }
}
