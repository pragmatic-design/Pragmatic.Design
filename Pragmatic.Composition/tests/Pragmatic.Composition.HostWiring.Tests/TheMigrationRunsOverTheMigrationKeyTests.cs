// Pragmatic.Composition.HostWiring.Tests - The migration connection key survives every [Include] arity

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     <c>MigrationConfigKey</c> is the reason <c>[PragmaticDatabase]</c> carries two keys: migrations
///     run on a DDL connection, everything else on a least-privilege DML one. The key has to survive
///     every arity of <c>[Include]</c>, including the three-arity form PRAG1607/PRAG1652 push callers
///     towards.
/// </summary>
/// <remarks>
///     <para>
///         Measured on the generated migration coordinator, which is where the key becomes an observable
///         connection string. Both consumers fall back to <c>ConfigKey</c> when the migration key is
///         unset, so a dropped value produces working code that quietly migrates over the wrong account:
///         there is no compile error and no diagnostic to assert on instead.
///     </para>
///     <para>
///         The module has an entity. A database with none behind it has no schema to migrate to and gets
///         no coordinator at all, which is why these tests do not live in the stub-only suite: that host
///         has no entity anywhere.
///     </para>
/// </remarks>
public sealed class TheMigrationRunsOverTheMigrationKeyTests
{
    private const string ConfigKey = "ConnectionStrings:App";
    private const string MigrationKey = "ConnectionStrings:App:Migration";

    private const string TwoArity = "[Include<LeaveModule, AppDatabase>]";
    private const string ThreeArity = "[Include<LeaveModule, AppDatabase, ExplicitLeaveDbContext>]";

    private static string Host(string include, string? migrationConfigKey) => $$"""
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Database;
        using Pragmatic.Composition.Enums;
        using TimeOff.Leave;

        namespace TimeOff.Host;

        [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql,
            ConfigKey = "{{ConfigKey}}"{{(migrationConfigKey is null
                ? ""
                : $", MigrationConfigKey = \"{migrationConfigKey}\"")}})]
        public sealed class AppDatabase : PragmaticDatabase;

        public sealed class ExplicitLeaveDbContext(DbContextOptions<ExplicitLeaveDbContext> options)
            : DbContext(options);

        [Module]
        {{include}}
        public sealed class TimeOffHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    /// <summary>The connection-string key the generated migration coordinator actually reads.</summary>
    private static string MigrationConnectionKeyOf(string include, string? migrationConfigKey = MigrationKey)
    {
        var (errors, generated) = ModuleAndHost.Generate(
            "TimeOff.Leave",
            [AModuleWithoutEntitiesOnADatabaseTests.ModuleWithoutEntities, AModuleWithoutEntitiesOnADatabaseTests.OneEntity],
            "TimeOff.Host",
            Host(include, migrationConfigKey));

        errors.Should().BeEmpty("the host has to compile, or the coordinator is text nobody runs");
        generated.TryGetValue("_Migration.Coordinator.g.cs", out var coordinator).Should().BeTrue(
            "the host includes a module with an entity on a database, so the migration coordinator is emitted");

        var line = coordinator!.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("configuration[", StringComparison.Ordinal));

        line.Should().NotBeNull("the coordinator resolves its connection string from IConfiguration");
        return line!;
    }

    [Fact]
    public void TwoArityInclude_MigratesOverTheMigrationConnection()
        => MigrationConnectionKeyOf(TwoArity).Should().Contain(MigrationKey);

    /// <summary>
    ///     The case that was broken: naming the DbContext explicitly must not change which account
    ///     the migration runs under.
    /// </summary>
    [Fact]
    public void ThreeArityInclude_MigratesOverTheMigrationConnection()
        => MigrationConnectionKeyOf(ThreeArity).Should().Contain(MigrationKey);

    /// <summary>
    ///     The documented fallback, kept as the record of what "unset" means. It is also what made the
    ///     defect invisible: with the key dropped, the three-arity form produced exactly this line.
    /// </summary>
    [Theory]
    [InlineData(TwoArity)]
    [InlineData(ThreeArity)]
    public void WithoutAMigrationKey_MigrationsUseTheConnectionKey(string include)
    {
        var line = MigrationConnectionKeyOf(include, migrationConfigKey: null);

        line.Should().Contain(ConfigKey);
        line.Should().NotContain(MigrationKey);
    }
}
