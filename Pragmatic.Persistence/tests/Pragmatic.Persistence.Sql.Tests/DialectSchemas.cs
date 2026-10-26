extern alias generator;

using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Migrations.Schema;
using Pragmatic.Persistence.Sql;

namespace PragmaticEntities.Tests;

/// <summary>
///     The schema of the suite's entities on each dialect, as the generator writes it for a host on that dialect's driver: a host's
///     <c>DefaultSchema</c> is typed for the one driver it references, and this suite migrates five databases. The generator runs in process
///     over the entities' source (<c>Entities.cs</c>, embedded) with the driver stood for by a type of the name it probes; the schema file is
///     compiled alone and read.
/// </summary>
internal static class DialectSchemas
{
    private const string Host = "public static class SchemaHost { public static void Main() { } }";

    /// <summary>The Sql engine's bridge, by the type the generator recognises it by.</summary>
    private const string SqlBridge = "namespace Pragmatic.Persistence.Sql { public static class PragmaticSqlFilters { } }";

    private static readonly ConcurrentDictionary<SqlDialect, SchemaVersion> Schemas = new();

    /// <summary>The database's schema on <paramref name="dialect" />.</summary>
    public static SchemaVersion For(SqlDialect dialect) => Schemas.GetOrAdd(dialect, Generate);

    private static SchemaVersion Generate(SqlDialect dialect)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Pragmatic.Persistence.Sql.Generated")]);
        var compilation = CSharpCompilation.Create("SchemaHost", new[] { Entities(), Host, SqlBridge, DriverProbe(dialect) }.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)),
            References([typeof(global::Pragmatic.Persistence.Entity.EntityAttribute), typeof(global::Pragmatic.MultiTenancy.ITenantEntity),
                typeof(global::Pragmatic.Events.IDomainEvent), typeof(SchemaVersion), typeof(SqlSession),
                typeof(global::Pragmatic.Comments.HasCommentsAttribute), typeof(global::Pragmatic.Notes.HasNotesAttribute), typeof(global::Pragmatic.Tags.HasTagsAttribute)]),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        CSharpGeneratorDriver.Create(new generator::Pragmatic.SourceGenerator.PragmaticSourceGenerator()).WithUpdatedParseOptions(parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);
        var source = generated.SyntaxTrees.SingleOrDefault(t => t.FilePath.EndsWith("_Infra.Persistence.SchemaMetadata.g.cs", StringComparison.Ordinal))?.ToString()
            ?? throw new InvalidOperationException($"The generator wrote no schema on {dialect}: " + string.Join(", ", generated.SyntaxTrees.Select(t => Path.GetFileName(t.FilePath))));

        var schema = CSharpCompilation.Create("Schema" + dialect, [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References([typeof(SchemaVersion)]), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emitted = schema.Emit(image);
        if (!emitted.Success)
            throw new InvalidOperationException($"The schema on {dialect} does not compile alone: " + string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var holder = Assembly.Load(image.ToArray()).GetTypes().Single(t => t.Name == "DefaultSchema");
        return (SchemaVersion)holder.GetField("Current", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    }

    /// <summary>The driver type the generator probes for each dialect.</summary>
    private static string DriverProbe(SqlDialect dialect) => dialect switch
    {
        SqlDialect.PostgreSql => "namespace Npgsql { public sealed class NpgsqlConnection { } }",
        SqlDialect.SqlServer => "namespace Microsoft.Data.SqlClient { public sealed class SqlConnection { } }",
        SqlDialect.MySql => "namespace MySqlConnector { public sealed class MySqlConnection { } }",
        SqlDialect.Oracle => "namespace Oracle.ManagedDataAccess.Client { public sealed class OracleConnection { } }",
        _ => "namespace Microsoft.Data.Sqlite { public sealed class SqliteConnection { } }",
    };

    private static string Entities()
    {
        using var stream = typeof(DialectSchemas).Assembly.GetManifestResourceStream("Entities.cs")!;
        using var reader = new StreamReader(stream);
        return "global using System; global using System.Collections.Generic;\n" + reader.ReadToEnd();
    }

    private static List<MetadataReference> References(IEnumerable<Type> runtime)
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll" or "Microsoft.Extensions.DependencyInjection.Abstractions.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Concat(runtime.Select(t => t.Assembly.Location).Distinct().Select(l => MetadataReference.CreateFromFile(l)))
            .ToList();
}
