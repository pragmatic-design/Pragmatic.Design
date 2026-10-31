using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Migrations.Schema;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>[SqlDatabase]</c> holds the Sql engine's own <c>[Entity]</c> rows. A Pragmatic entity's table is in the host's <c>DefaultSchema</c>, with
///     what its boundary adds, so one that names a database with <c>[StoredIn&lt;T&gt;]</c> is refused (PRAG3027) rather than described a second time
///     by another path, or silently ignored.
/// </summary>
public class ASqlDatabaseHoldsTheEnginesRowsOnlyTests
{
    private const string Host = "public static class SchemaProbeHost { public static void Main() { } }";

    private const string SqlBridge = "namespace Pragmatic.Persistence.Sql { public static class PragmaticSqlFilters { } }";

    private const string Driver = "namespace Npgsql { public sealed class NpgsqlConnection { } }";

    [Fact]
    public void APragmaticEntityStoredInADeclaredDatabase_IsRefused()
    {
        var diagnostics = Run(Entities("[Pragmatic.Persistence.Sql.Attributes.StoredIn<TicketDatabase>]"));

        diagnostics.Should().Contain(d => d.Id == "PRAG3027" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Order", StringComparison.Ordinal),
            "a Pragmatic entity's table is in DefaultSchema, not a [SqlDatabase]'s");
    }

    /// <summary>The control: without the attribute the same assembly, one database and a Pragmatic entity, is not refused.</summary>
    [Fact]
    public void APragmaticEntityBesideADeclaredDatabase_IsNotRefused()
    {
        Run(Entities("")).Should().NotContain(d => d.Id == "PRAG3027");
    }

    /// <summary>A Pragmatic entity carrying <paramref name="storedIn" />, beside an engine row and the one database that stores it.</summary>
    private static string Entities(string storedIn) => $$"""
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe;

        [Pragmatic.Persistence.Sql.Attributes.SqlDatabase]
        public sealed partial class TicketDatabase;

        [Pragmatic.Persistence.Sql.Attributes.Entity]
        public sealed class Ticket
        {
            public long Id { get; set; }

            public string Subject { get; set; } = "";
        }

        [Entity]
        {{storedIn}}
        public partial class Order : IEntity
        {
            public string Code { get; private set; } = "";
        }

        // A read of the entity on the engine, as an application has one: what brings it into the engine's model.
        public static class Orders
        {
            public static System.Threading.Tasks.Task<long> CountAsync(Pragmatic.Persistence.Sql.SqlSession session) => session.Query<Order>().CountAsync();
        }
        """;

    private static ImmutableArray<Diagnostic> Run(string entities)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Pragmatic.Persistence.Sql.Generated")]);
        var compilation = CSharpCompilation.Create("DatabaseProbe", new[] { entities, Host, SqlBridge, Driver }.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)),
            References([typeof(global::Pragmatic.Persistence.Sql.SqlSession), typeof(global::Pragmatic.Persistence.Entity.EntityAttribute), typeof(SchemaVersion)]),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        CSharpGeneratorDriver.Create(new PragmaticSourceGenerator()).WithUpdatedParseOptions(parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics;
    }

    private static List<MetadataReference> References(IEnumerable<Type> runtime)
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll" or "Microsoft.Extensions.DependencyInjection.Abstractions.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Concat(runtime.Select(t => t.Assembly.Location).Distinct().Select(l => MetadataReference.CreateFromFile(l)))
            .ToList();
}
