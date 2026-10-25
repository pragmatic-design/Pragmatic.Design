using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Migrations.Schema;
using Pragmatic.Testing.Assertions;
using Xunit;
using Xunit.Abstractions;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The schema the Sql engine describes for Pragmatic's entities against the one EF Core's branch describes for Pragmatic.Migrations, on
///     the entities the Sql bridge's suite migrates: line for line, but for the departures listed here. Both are the database's
///     <c>DefaultSchema.Current</c>, of a host whose boundaries one engine persists, then the other.
/// </summary>
/// <remarks>
///     <para>
///         Each departure is a place where the two branches describe the same entity differently. The SQL Server ones that were defects of
///         EF Core's branch (an unbounded indexed string, a <c>false</c> default: neither migrates) are fixed there; what is left is
///         <c>[ConcurrencyAware]</c>'s column, a column of its own on the Sql engine and a shadow property EF Core's schema does not list.
///     </para>
///     <para>
///         EF Core's branch emits the schema in a host (an executable) that references Pragmatic.Migrations, with EF Core referenced and a
///         provider package: both are stood for by a type of the name the generator probes. Each schema file is compiled alone and read.
///     </para>
/// </remarks>
public class TheSqlEnginesSchemaIsPragmaticsTests(ITestOutputHelper output)
{
    private const string EfCoreGate = "namespace Pragmatic.Persistence.EFCore { public sealed class PragmaticDbContextAttribute : System.Attribute { } }";

    private const string Host = "public static class SchemaProbeHost { public static void Main() { } }";

    /// <summary>The Sql engine's bridge, by the type the generator recognises it by.</summary>
    private const string SqlBridge = "namespace Pragmatic.Persistence.Sql { public static class PragmaticSqlFilters { } }";

    /// <summary>Per provider, the lines only EF Core's schema has (-) and only the Sql engine's has (+).</summary>
    private static readonly Dictionary<string, string[]> Departures = new()
    {
        ["PostgreSql"] = [],
        ["SqlServer"] =
        [
            // [ConcurrencyAware]: a column of the database's own on the Sql engine; EF Core keeps a shadow property, which its schema does
            // not list. timestamp is rowversion's name in SQL Server's catalog: what the introspector reads back.
            "+ column Amenities.RowVersion timestamp NOT NULL",
        ],
        ["Sqlite"] =
        [
            "+ column Amenities.RowVersion INTEGER NOT NULL", // [ConcurrencyAware], as above: a bigint the writes advance
        ],
    };

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void TheSqlEnginesSchema_IsEfCoresBranch_ButForItsDepartures(string provider)
    {
        var entities = SqlPragmaticEntities();
        var efCore = Describe(EfCoreSchema(provider, entities)).ToList();
        var (sql, diagnostics) = SqlSchema(provider, entities); // the provider's name is the dialect's
        diagnostics.Should().NotContain(d => d.Id == "PRAG3027", "every entity has a schema on the Sql engine");
        sql.ProviderName.Should().Be(provider);
        var engine = Describe(sql).ToList();

        var differences = efCore.Except(engine).Select(l => "- " + l.Trim()).Concat(engine.Except(efCore).Select(l => "+ " + l.Trim())).ToList();
        foreach (var line in differences)
            output.WriteLine(line);
        differences.Order(StringComparer.Ordinal).ToList().Should().Equal(Departures[provider].Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    ///     EF Core's branch on SQL Server alone, as the comparison above cannot see it (both branches wrong alike would still compare equal):
    ///     no unbounded string a key or an index holds, no <c>false</c> default. Either fails the migration (<c>PragmaticMigrationTests</c>).
    /// </summary>
    [Fact]
    public void EfCoresBranch_OnSqlServer_WritesWhatSqlServerMigrates()
    {
        var schema = EfCoreSchema("SqlServer", SqlPragmaticEntities());
        foreach (var table in schema.Tables)
        {
            var keyed = table.Indexes.SelectMany(i => i.Columns).Concat(table.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name)).ToHashSet(StringComparer.Ordinal);
            table.Columns.Where(c => keyed.Contains(c.Name) && c.SqlType == "nvarchar(max)").Select(c => $"{table.Name}.{c.Name}").Should().BeEmpty("SQL Server indexes no nvarchar(max)");
            table.Columns.Where(c => c.DefaultValue == "false").Select(c => $"{table.Name}.{c.Name}").Should().BeEmpty("T-SQL has no boolean literal");
        }

        schema.Tables.SelectMany(t => t.Columns).Should().Contain(c => c.Name == "IsDeleted" && c.DefaultValue == "0", "the control: a soft-deleted entity is there");
    }

    /// <summary>A soft-deleted entity with a domain key: its unique index holds the live rows only, a filter on the soft-delete column.</summary>
    private const string KeyedEntity = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Entities;

        [Entity]
        [SoftDelete]
        public partial class Coupon : IEntity
        {
            [LogicKey] public string Code { get; private set; } = "";
        }
        """;

    /// <summary>
    ///     The domain key's unique index on the Sql engine is EF Core's: the same columns, the same name, the live rows only. Not in the bridge
    ///     suite's entities, whose databases include MySQL and Oracle, which have no partial index to migrate a filtered one to.
    /// </summary>
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void ALogicKey_IsTheSameUniqueIndexOnBothBranches(string provider)
    {
        var efCore = Describe(EfCoreSchema(provider, KeyedEntity)).ToList();
        var (sql, diagnostics) = SqlSchema(provider, KeyedEntity);
        diagnostics.Should().NotContain(d => d.Id == "PRAG3027", "a [LogicKey] is described, not refused");
        var engine = Describe(sql).ToList();

        efCore.Should().Contain(l => l.Contains("index IX_Coupons_Code (Code) UNIQUE", StringComparison.Ordinal), "the control: the key's index is there");
        efCore.Except(engine).Concat(engine.Except(efCore)).Should().BeEmpty();
    }

    /// <summary>
    ///     EF Core's branch on SQL Server writes the live rows' filter in T-SQL: <c>"IsDeleted" = 0</c>. With <c>false</c>, which T-SQL does not
    ///     have, the index fails to create and the migration with it, as the soft-delete column's default did (<see cref="EfCoresBranch_OnSqlServer_WritesWhatSqlServerMigrates" />).
    /// </summary>
    [Fact]
    public void EfCoresBranch_OnSqlServer_FiltersTheLiveRowsInTSql()
    {
        var index = EfCoreSchema("SqlServer", KeyedEntity).Tables.Single(t => t.Name == "Coupons").Indexes.Single(i => i.IsUnique);

        index.Filter.Should().Be("\"IsDeleted\" = 0");
    }

    /// <summary>The control: the schemas are not empty, so an equality of nothing to nothing cannot pass.</summary>
    [Fact]
    public void BothSchemas_DescribeTheEntitiesTables()
    {
        var entities = SqlPragmaticEntities();
        Describe(EfCoreSchema("PostgreSql", entities)).Should().Contain(l => l.StartsWith("table ", StringComparison.Ordinal) && l.EndsWith("Reviews", StringComparison.Ordinal));
        Describe(SqlSchema("PostgreSql", entities).Schema).Should().Contain(l => l.StartsWith("table ", StringComparison.Ordinal) && l.EndsWith("Reviews", StringComparison.Ordinal));
    }

    private static string SqlPragmaticEntities()
    {
        using var stream = typeof(TheSqlEnginesSchemaIsPragmaticsTests).Assembly.GetManifestResourceStream("SqlPragmaticEntities.cs")!;
        using var reader = new StreamReader(stream);
        return "global using System; global using System.Collections.Generic;\n" + reader.ReadToEnd();
    }

    /// <summary>The type the generator probes for each EF Core provider (<c>FeatureDetector.DetectEfCoreProvider</c>).</summary>
    private static string ProviderProbe(string provider) => provider switch
    {
        "PostgreSql" => "namespace Microsoft.EntityFrameworkCore { public static class NpgsqlDbContextOptionsBuilderExtensions { } }",
        "SqlServer" => "namespace Microsoft.EntityFrameworkCore { public static class SqlServerDbContextOptionsBuilderExtensions { } }",
        _ => "namespace Microsoft.EntityFrameworkCore { public static class SqliteDbContextOptionsBuilderExtensions { } }",
    };

    /// <summary>The schema EF Core's branch describes for <paramref name="entities" /> on <paramref name="provider" />.</summary>
    private static SchemaVersion EfCoreSchema(string provider, string entities)
    {
        var (generated, _) = Run([entities, EfCoreGate, Host, ProviderProbe(provider)], []);
        return Read(Generated(generated, "_Infra.Persistence.SchemaMetadata.g.cs"), "DefaultSchema", "Current");
    }

    /// <summary>
    ///     The schema the Sql engine describes for <paramref name="entities" /> on <paramref name="dialect" />, and what was reported: the host's
    ///     database schema, typed for the driver the compilation references (a type of the name the generator probes).
    /// </summary>
    private static (SchemaVersion Schema, ImmutableArray<Diagnostic> Diagnostics) SqlSchema(string dialect, string entities)
    {
        var (generated, diagnostics) = Run([entities, Host, SqlBridge, DriverProbe(dialect)], [typeof(global::Pragmatic.Persistence.Sql.SqlSession)]);
        return (Read(Generated(generated, "_Infra.Persistence.SchemaMetadata.g.cs"), "DefaultSchema", "Current"), diagnostics);
    }

    /// <summary>The driver type the Sql engine's schema probes for each dialect (<c>SqlInterceptorGenerator.SchemaDialect</c>).</summary>
    private static string DriverProbe(string dialect) => dialect switch
    {
        "PostgreSql" => "namespace Npgsql { public sealed class NpgsqlConnection { } }",
        "SqlServer" => "namespace Microsoft.Data.SqlClient { public sealed class SqlConnection { } }",
        _ => "namespace Microsoft.Data.Sqlite { public sealed class SqliteConnection { } }",
    };

    private static string Generated(Compilation output, string hint)
        => output.SyntaxTrees.SingleOrDefault(t => t.FilePath.EndsWith(hint, StringComparison.Ordinal))?.ToString()
           ?? throw new InvalidOperationException($"The generator emitted no {hint}: " + string.Join(", ", output.SyntaxTrees.Select(t => Path.GetFileName(t.FilePath))));

    /// <summary><paramref name="sources" /> compiled as a host and run through the generator.</summary>
    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) Run(string[] sources, Type[] runtime)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Pragmatic.Persistence.Sql.Generated")]);
        var compilation = CSharpCompilation.Create("SchemaProbe", sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)),
            References(runtime.Concat([typeof(global::Pragmatic.Persistence.Entity.EntityAttribute), typeof(global::Pragmatic.MultiTenancy.ITenantEntity),
                typeof(global::Pragmatic.Events.IDomainEvent), typeof(SchemaVersion)])),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        CSharpGeneratorDriver.Create(new PragmaticSourceGenerator()).WithUpdatedParseOptions(parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        return (generated, diagnostics);
    }

    /// <summary>
    ///     The static <paramref name="member" /> of <paramref name="type" /> in <paramref name="source" />, compiled alone: a field or property,
    ///     or a method called with the enum value <paramref name="argument" /> names.
    /// </summary>
    private static SchemaVersion Read(string source, string type, string member, string? argument = null)
    {
        var compilation = CSharpCompilation.Create("Schema" + Guid.NewGuid().ToString("N"), [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References([typeof(SchemaVersion), typeof(global::Pragmatic.Persistence.Sql.SqlSession)]),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
            throw new InvalidOperationException("The schema does not compile alone: " + string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var holder = Assembly.Load(image.ToArray()).GetTypes().Single(t => t.Name == type);
        const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;
        var method = argument is null ? null : holder.GetMethod(member, Static)!;
        var value = method is not null ? method.Invoke(null, [Enum.Parse(method.GetParameters()[0].ParameterType, argument!)])
            : holder.GetField(member, Static)?.GetValue(null) ?? holder.GetProperty(member, Static)!.GetValue(null);
        return (SchemaVersion)value!;
    }

    /// <summary><paramref name="schema" /> one line per table, column, index, foreign key and check, in name order: what the diff compares.</summary>
    private static IEnumerable<string> Describe(SchemaVersion schema)
    {
        foreach (var table in schema.Tables.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var name = table.SchemaName is null ? table.Name : table.SchemaName + "." + table.Name;
            yield return $"table {name}";
            foreach (var column in table.Columns.OrderBy(c => c.Name, StringComparer.Ordinal))
                yield return $"  column {table.Name}.{column.Name} {column.SqlType}{(column.IsNullable ? " NULL" : " NOT NULL")}{(column.IsPrimaryKey ? " PK" : "")}{(column.DefaultValue is null ? "" : " DEFAULT " + column.DefaultValue)}";
            foreach (var index in table.Indexes.OrderBy(i => i.Name, StringComparer.Ordinal))
                yield return $"  index {index.Name} ({string.Join(", ", index.Columns)}){(index.IsUnique ? " UNIQUE" : "")}{(index.Filter is null ? "" : " WHERE " + index.Filter)}";
            foreach (var key in table.ForeignKeys.OrderBy(f => f.Name, StringComparer.Ordinal))
                yield return $"  fk {key.Name} {key.Column} -> {key.ReferencedTable}.{key.ReferencedColumn} {key.OnDelete}";
            foreach (var check in (table.CheckConstraints.IsDefault ? [] : table.CheckConstraints).OrderBy(c => c.Name, StringComparer.Ordinal))
                yield return $"  check {check.Name} {check.Expression}";
        }
    }

    private static List<MetadataReference> References(IEnumerable<Type> runtime)
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll" or "Microsoft.Extensions.DependencyInjection.Abstractions.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Concat(runtime.Select(t => t.Assembly.Location).Distinct().Select(l => MetadataReference.CreateFromFile(l)))
            .ToList();
}
