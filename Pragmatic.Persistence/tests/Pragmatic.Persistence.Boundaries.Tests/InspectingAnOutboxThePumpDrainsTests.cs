using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Engines.Documents;
using Pragmatic.Messaging.Engines.Sql;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     <c>InspectPendingAsync</c> read while the delivery pump drains the outbox, on the Sql and the documents engine: the pump marks the last
///     pending message processed after the source counted it and before it read the oldest. The write is put exactly there, on every run (a
///     connection that runs it before the second statement on the outbox, a relay before the second command reaches MongoDB), and the source
///     answers "nothing pending" instead of throwing.
/// </summary>
public sealed class InspectingAnOutboxThePumpDrainsTests(MongoServer mongo) : IClassFixture<MongoServer>
{
    private const string OutboxTable = """
        CREATE TABLE "__OutboxMessages" ("Id" TEXT PRIMARY KEY, "MessageType" TEXT NOT NULL, "Payload" TEXT NOT NULL, "CreatedAt" INTEGER NOT NULL,
            "ProcessedAt" INTEGER NULL, "RetryCount" INTEGER NOT NULL, "NextAttemptAt" INTEGER NULL, "Error" TEXT NULL, "ClaimedBy" TEXT NULL,
            "ClaimedUntil" INTEGER NULL, "CorrelationId" TEXT NULL, "TenantId" TEXT NULL, "UserId" TEXT NULL, "HeadersJson" TEXT NULL);
        INSERT INTO "__OutboxMessages" ("Id", "MessageType", "Payload", "CreatedAt", "RetryCount") VALUES ('7d1c3f0e-0000-0000-0000-000000000001', 'OrderPlaced', '{}', 1, 0);
        """;

    [Fact]
    public async Task On_the_sql_engine_a_message_processed_between_the_count_and_the_oldest_leaves_nothing_pending()
    {
        using var keeper = new SqliteConnection($"Data Source=i{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        keeper.Open();
        Execute(keeper, OutboxTable);
        var connection = new BeforeACommandConnection(keeper);
        using var session = new SqlSession(connection, SqlDialect.Sqlite);
        var source = new SqlOutboxSource(session, "Orders", NullLogger<SqlOutboxSource>.Instance);

        var drained = false;
        // The second command on the outbox, the oldest's read: the first is the count.
        connection.Before("__OutboxMessages", 2, inner =>
        {
            Execute(inner, "UPDATE \"__OutboxMessages\" SET \"ProcessedAt\" = 2");
            drained = true;
        });
        var inspected = await source.InspectPendingAsync();

        Assert.True(drained, "no second command reached the outbox: the race was not held open");
        Assert.Equal((0, (DateTimeOffset?)null), inspected);
    }

    [Fact]
    public async Task On_the_documents_engine_a_message_processed_between_the_count_and_the_oldest_leaves_nothing_pending()
    {
        var database = "i" + Guid.NewGuid().ToString("N")[..12];
        var direct = new DocSession(mongo.Client, database);
        await direct.InsertAsync(new OutboxMessageDocument { Id = Guid.NewGuid(), MessageType = "ShipmentSent", Payload = "{}", CreatedAt = 1 });

        await using var relay = MongoCommandRelay.Start(mongo.Client.Settings.Host, mongo.Client.Settings.Port);
        await using var relayed = new MongoClient(mongo.Client.Settings with { Host = "127.0.0.1", Port = relay.Port });
        var source = new DocOutboxSource(new DocSession(relayed, database), "Shipments", NullLogger<DocOutboxSource>.Instance);

        var drained = false;
        // The second command on the outbox, the oldest's read: the first is the count.
        relay.Before("__OutboxMessages", 2, async () =>
        {
            await direct.Query<OutboxMessageDocument>().ExecuteUpdateAsync((u, at) => u.Set(d => d.ProcessedAt, at), (long?)2);
            drained = true;
        });
        var inspected = await source.InspectPendingAsync();

        Assert.True(drained, "no second command reached the outbox: the race was not held open");
        Assert.Equal((0, (DateTimeOffset?)null), inspected);
    }

    private static void Execute(System.Data.Common.DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
