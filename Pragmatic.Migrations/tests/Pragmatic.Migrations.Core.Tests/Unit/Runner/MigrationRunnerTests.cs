using System.Collections.Immutable;
using System.Data.Common;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Core.Tests.Unit.Runner;

public class MigrationRunnerTests
{
    private readonly SchemaIntrospectorMock _introspector = new SchemaIntrospectorMock();
    private readonly SchemaDiffEngineMock _diffEngine = new SchemaDiffEngineMock();
    private readonly SqlMigrationGeneratorMock _sqlGenerator = new SqlMigrationGeneratorMock();

    private MigrationRunner CreateRunner()
    {
        // Build a real MigrationProviderFactory backed by mocks
        var connectionFactory = new ConnectionFactoryMock();
        // The mock simulates a SQLite factory — report its provider so the runner resolves the matching
        // leader-election strategy (AlwaysLeader for single-process SQLite). An unconfigured mock returns a
        // null provider, which is not what a SQLite factory reports: the runner must not pick a
        // wrong-dialect distributed election that can never acquire and then waits forever.
        connectionFactory.ProviderName.Returns("Sqlite");
        // A real SQLite connection (never queried — the introspector is mocked) rather than a mocked
        // DbConnection: the runner owns and disposes it (`await using`), and this exercises that.
        connectionFactory.CreateOpenConnectionAsync.Returns((_, _) =>
        {
            DbConnection connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return Task.FromResult(connection);
        });

        // Registered AS the interfaces. The variables are generated mock classes now, and
        // AddSingleton infers the service type from the variable — which would register the mock
        // type and leave the interface unbound, with the failure surfacing far away as
        // "no provider registered".
        var services = new ServiceCollection();
        services.AddSingleton<ISchemaIntrospector>(_introspector);
        services.AddSingleton<ISqlMigrationGenerator>(_sqlGenerator);
        services.AddSingleton<IConnectionFactory>(connectionFactory);
        var sp = services.BuildServiceProvider();
        var factory = new MigrationProviderFactory(sp);
        return new MigrationRunner(factory, _diffEngine);
    }

    [Fact]
    public async Task MigrateAsync_IdenticalHashes_SkipsTheDiff()
    {
        // Both hashes come from SchemaHasher, which normalises exactly what the diff compares, so
        // equal hashes prove the diff would find nothing. (SchemaHasherTests pins that property.)
        var schema = new SchemaVersion([
            new TableSchema("Users", null, [new ColumnSchema("Id", "uuid", false, true)], [], [])
        ]);
        _introspector.IntrospectAsync.Returns(schema);

        var runner = CreateRunner();
        var context = new MigrationContext("", schema, new MigrationOptions());

        var result = await runner.MigrateAsync(context);

        result.Success.Should().BeTrue();
        result.ChangesApplied.Should().Be(0);
        _diffEngine.ComputeDiff.DidNotReceive();
    }

    [Fact]
    public async Task MigrateAsync_ManageDeclaredTablesOnly_AlwaysDiffsEvenOnEqualHashes()
    {
        // With that option the desired schema is deliberately a subset of the database, so the two
        // identities are not meant to match and the shortcut must not apply.
        var schema = new SchemaVersion([
            new TableSchema("Users", null, [new ColumnSchema("Id", "uuid", false, true)], [], [])
        ]);
        _introspector.IntrospectAsync.Returns(schema);
        _diffEngine.ComputeDiff.Returns(SchemaDiff.Empty);

        var runner = CreateRunner();
        var context = new MigrationContext("", schema, new MigrationOptions { ManageDeclaredTablesOnly = true });

        await runner.MigrateAsync(context);

        _diffEngine.ComputeDiff.Received(1);
    }

    [Fact]
    public async Task MigrateAsync_NoChangesInDiff_ReturnsNoChanges()
    {
        _introspector.IntrospectAsync.Returns(new SchemaVersion([]));
        _diffEngine.ComputeDiff.Returns(SchemaDiff.Empty);

        var runner = CreateRunner();
        var context = new MigrationContext("", DesiredWithOneTable(), new MigrationOptions());

        var result = await runner.MigrateAsync(context);

        result.Success.Should().BeTrue();
        result.ChangesApplied.Should().Be(0);
    }

    /// <summary>A desired schema that cannot hash equal to the empty introspected one.</summary>
    private static SchemaVersion DesiredWithOneTable() => new([
        new TableSchema("Users", null, [new ColumnSchema("Id", "uuid", false, true)], [], [])
    ]);

    [Fact]
    public async Task MigrateAsync_DryRun_ReturnsSqlWithoutExecuting()
    {
        // The two sides must genuinely differ: identical schemas hash identically and the runner
        // (correctly) short-circuits before reaching the diff.
        _introspector.IntrospectAsync.Returns(new SchemaVersion([]));

        var changes = ImmutableArray.Create<SchemaChange>(
            new AddColumn("Users", new ColumnSchema("Email", "text", true, false)));

        _diffEngine.ComputeDiff.Returns(new SchemaDiff(changes, false));
        _sqlGenerator.GenerateScript.Returns("ALTER TABLE ...");

        var runner = CreateRunner();
        var context = new MigrationContext(null!, DesiredWithOneTable(), new MigrationOptions { DryRun = true });

        var result = await runner.MigrateAsync(context);

        result.Success.Should().BeTrue();
        result.ChangesApplied.Should().Be(0); // Dry run — no changes actually applied
        result.GeneratedSql.Should().Be("ALTER TABLE ...");
        result.AppliedChanges.Should().HaveCount(1); // Changes are still reported
    }

    [Fact]
    public async Task MigrateAsync_BreakingChangesWithoutForce_ReturnsError()
    {
        _introspector.IntrospectAsync.Returns(new SchemaVersion([]));

        var changes = ImmutableArray.Create<SchemaChange>(
            new DropTable("OldTable"));

        _diffEngine.ComputeDiff.Returns(new SchemaDiff(changes, true));
        _sqlGenerator.GenerateScript.Returns("DROP TABLE ...");

        var runner = CreateRunner();
        var context = new MigrationContext(null!, DesiredWithOneTable(), new MigrationOptions { Force = false });

        var result = await runner.MigrateAsync(context);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Breaking changes detected");
        result.Suggestions.Should().NotBeEmpty();
    }
}
