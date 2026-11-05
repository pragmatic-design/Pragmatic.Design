using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Engines.Documents;
using Pragmatic.Notifications.Engines.Sql;
using Pragmatic.Notifications.Tracking;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Schema;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     The notification store of a <c>[StoresNotifications]</c> boundary on each engine, as the generated <c>Add{Boundary}Persistence</c>
///     registers it: the notices' records in their SQLite database, the bulletins' in their MongoDB database, each through its boundary's
///     session; and the bulletins' generated <c>{Boundary}DocSchema</c> holding the collection the package declares.
/// </summary>
public sealed class NotificationStoreOnTwoEnginesTests(MongoServer mongo) : IClassFixture<MongoServer>
{
    // The notices' tables as SQLite holds them (__Notifications is EF Core's: the enums by name, the instants as text).
    private const string Schema = """
        CREATE TABLE "Notices" ("PersistenceId" TEXT PRIMARY KEY, "Title" TEXT NOT NULL);
        CREATE TABLE "__Notifications" ("Id" TEXT PRIMARY KEY, "Audience" TEXT NOT NULL, "RecipientAddress" TEXT NOT NULL, "Channel" TEXT NOT NULL,
            "Subject" TEXT NOT NULL, "Status" TEXT NOT NULL, "ProviderId" TEXT NULL, "ErrorMessage" TEXT NULL, "TenantId" TEXT NULL, "Category" TEXT NULL,
            "CreatedAt" TEXT NOT NULL, "SentAt" TEXT NULL, "DeliveredAt" TEXT NULL, "ReadAt" TEXT NULL, "MetadataJson" TEXT NULL);
        """;

    [Fact]
    public async Task On_the_Sql_engine_the_generated_registration_keeps_the_records_in_the_boundarys_database()
    {
        using var notices = Notices();
        var services = Services();
        services.AddNoticeBoardBoundaryPersistence((_, filters) => new SqlSession(Open(notices.ConnectionString), SqlDialect.Sqlite) { Filters = filters });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var store = provider.GetRequiredService<INotificationStore>();
        Assert.IsType<SqlNotificationStore>(store);
        var record = await store.CreateAsync(Record());
        await store.UpdateStatusAsync(record.Id, DeliveryStatus.Sent, null);

        Assert.Equal(DeliveryStatus.Sent, (await store.GetByIdAsync(record.Id))?.Status);
        Assert.Equal(1, Scalar(notices, "SELECT COUNT(*) FROM \"__Notifications\" WHERE \"Status\" = 'Sent' AND \"SentAt\" IS NOT NULL"));
    }

    [Fact]
    public async Task On_the_documents_engine_the_generated_registration_and_schema_keep_the_records_in_the_boundarys_database()
    {
        var database = "n" + Guid.NewGuid().ToString("N")[..12];
        var services = Services();
        services.AddMailroomBoundaryPersistence((_, filters) => new DocSession(mongo.Client, database) { Filters = filters });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var collection = Assert.Single(MailroomBoundaryDocSchema.Collections, c => c.Collection == "__Notifications");
        Assert.Same(NotificationDocument.Schema, collection);
        await using (var scope = provider.CreateAsyncScope())
            await DocSchema.EnsureAsync(scope.ServiceProvider.GetRequiredKeyedService<DocSession>(typeof(MailroomBoundary)), MailroomBoundaryDocSchema.Collections);

        var store = provider.GetRequiredService<INotificationStore>();
        Assert.IsType<DocNotificationStore>(store);
        var record = await store.CreateAsync(Record());
        await store.UpdateStatusAsync(record.Id, DeliveryStatus.Sent, null);

        Assert.Equal(DeliveryStatus.Sent, (await store.GetByIdAsync(record.Id))?.Status);
        await using var reading = provider.CreateAsyncScope();
        var stored = Assert.Single(await reading.ServiceProvider.GetRequiredKeyedService<DocSession>(typeof(MailroomBoundary)).Query<NotificationDocument>().ToListAsync());
        Assert.Equal(("Sent", true), (stored.Status, stored.SentAt is not null));
    }

    private static NotificationRecord Record() => new()
    {
        Audience = NotificationAudience.EndUser,
        RecipientAddress = "anna@example.com",
        Channel = NotificationChannel.Email,
        Subject = "Your notice",
        Status = DeliveryStatus.Pending,
    };

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new TestUser("anna"));
        services.AddSingleton<ITenantContext>(new TenantScope());
        services.AddScoped<IQueryFilterToggle, QueryFilterToggle>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        return services;
    }

    private static SqliteConnection Open(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>An in-memory SQLite database of the test's own with the notices' tables, kept alive by the returned connection.</summary>
    private static SqliteConnection Notices()
    {
        var keeper = new SqliteConnection($"Data Source=n{Guid.NewGuid().ToString("N")[..12]};Mode=Memory;Cache=Shared");
        keeper.Open();
        using var create = keeper.CreateCommand();
        create.CommandText = Schema;
        create.ExecuteNonQuery();
        return keeper;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
