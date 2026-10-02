using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     The generated entry checks every connection string a database declares before the first line
///     that uses one.
/// </summary>
/// <remarks>
///     The generator knows the key at compile time — it is on <c>[PragmaticDatabase(ConfigKey = …)]</c> —
///     so it checks it up front. A read that turned its absence into <c>""</c> would fail wherever the
///     empty value was first used: leader election, then migration polling, then the schema check,
///     each saying something other than "this key is missing".
/// </remarks>
public class DeclaredConnectionStringsAreCheckedFirstTests
{
    private const string Stubs = """
        namespace Pragmatic.Composition.Hosting { public class PragmaticBuilder { } }

        namespace Pragmatic.Migrations.Schema { public sealed class SchemaVersion { } }

        namespace Pragmatic.Composition.Database
        {
            public abstract class PragmaticDatabase { }
        }

        namespace Pragmatic.Composition.Enums
        {
            public enum DatabaseProvider { SqlServer, PostgreSql, SQLite, MySql, InMemory }
        }

        namespace Pragmatic.Composition.Attributes
        {
            using Pragmatic.Composition.Database;
            using Pragmatic.Composition.Enums;

            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
            public sealed class PragmaticDatabaseAttribute : System.Attribute
            {
                public DatabaseProvider Provider { get; set; }
                public string ConfigKey { get; set; } = "";
                public string? MigrationConfigKey { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
            public sealed class ModuleAttribute : System.Attribute
            {
                public string? Name { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class IncludeAttribute<TModule, TDatabase> : System.Attribute
                where TModule : class where TDatabase : PragmaticDatabase { }
        }
        """;

    private static string Host(string provider, string? migrationConfigKey = null) => $$"""

        namespace App
        {
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Database;
            using Pragmatic.Composition.Enums;

            public sealed class SalesBoundary { }

            [PragmaticDatabase(Provider = DatabaseProvider.{{provider}}, ConfigKey = "ConnectionStrings:App"{{(
                migrationConfigKey is null ? "" : $", MigrationConfigKey = \"{migrationConfigKey}\"")}})]
            public sealed class AppDatabase : PragmaticDatabase { }

            [Module(Name = "AppHost")]
            [Include<SalesBoundary, AppDatabase>]
            public sealed class HostModule { }
        }

        internal static class Program { private static void Main() { } }
        """;

    private static string EntryOf(string provider, string? migrationConfigKey = null)
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Stubs + Host(provider, migrationConfigKey));

        var entry = GeneratorTestHelper.GetGeneratedSource(result, "Host.Entry");
        entry.Should().NotBeNull("a host with an [Include] generates its entry point");
        return entry!;
    }

    [Fact]
    public void ADatabaseWithAConfigKey_IsCheckedBeforeItsFirstUse()
    {
        var entry = EntryOf("PostgreSql");

        var check = entry.IndexOf(
            "DeclaredConnectionStrings.Require(app.Configuration, \"ConnectionStrings:App\", \"AppDatabase\"",
            StringComparison.Ordinal);
        check.Should().BeGreaterThan(-1, "the declared key is checked in the web entry");

        var firstUse = entry.IndexOf("[\"ConnectionStrings:App\"]", StringComparison.Ordinal);
        if (firstUse >= 0)
            check.Should().BeLessThan(firstUse, "the check comes before the first read that uses the value");
    }

    [Fact]
    public void AMigrationKey_IsCheckedToo()
    {
        var entry = EntryOf("PostgreSql", "ConnectionStrings:AppMigrations");

        entry.Should().Contain("DeclaredConnectionStrings.Require(app.Configuration, \"ConnectionStrings:AppMigrations\", \"AppDatabase\"");
    }

    /// <summary>
    ///     The control: an in-memory database has no connection string, and a check that fired for it
    ///     would stop every test host that uses one.
    /// </summary>
    [Fact]
    public void AnInMemoryDatabase_IsNotChecked()
    {
        var entry = EntryOf("InMemory");

        entry.Should().NotContain("DeclaredConnectionStrings.Require");
    }
}
