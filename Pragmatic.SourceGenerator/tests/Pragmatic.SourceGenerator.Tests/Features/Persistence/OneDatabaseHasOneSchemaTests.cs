using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.Migrations.Schema;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A host whose one database holds a boundary EF Core persists and one the Sql engine persists: one schema for that database, with the
///     tables of both, which is what Pragmatic.Migrations migrates it to.
/// </summary>
/// <remarks>
///     EF Core and the Sql engine's bridge are stood for by the types the generator probes, as in <c>TheSqlEnginesSchemaIsPragmaticsTests</c>.
/// </remarks>
public class OneDatabaseHasOneSchemaTests
{
    private const string EfCoreGate = "namespace Pragmatic.Persistence.EFCore { public sealed class PragmaticDbContextAttribute : System.Attribute { } }";

    private const string PostgreSql = "namespace Microsoft.EntityFrameworkCore { public static class NpgsqlDbContextOptionsBuilderExtensions { } }";

    private const string SqlBridge = "namespace Pragmatic.Persistence.Sql { public static class PragmaticSqlFilters { } }";

    private const string Host = "public static class SchemaProbeHost { public static void Main() { } }";

    private const string TwoEngines = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Catalog
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.EfCore)] public partial class CatalogBoundary;
        }

        namespace Shop.Catalog.Entities
        {
            [Entity] [BelongsTo<Shop.Catalog.CatalogBoundary>]
            public partial class Product : IEntity { public string Name { get; set; } = ""; }
        }

        namespace Shop.Reviews
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)] public partial class ReviewsBoundary;
        }

        namespace Shop.Reviews.Entities
        {
            [Entity] [BelongsTo<Shop.Reviews.ReviewsBoundary>]
            public partial class Review : IEntity { public string Text { get; set; } = ""; }
        }
        """;

    [Fact]
    public void ADatabaseOfTwoEngines_HasOneSchema_WithTheTablesOfBoth()
    {
        var output = Run([TwoEngines, EfCoreGate, PostgreSql, SqlBridge, Host]);

        var schemas = output.SyntaxTrees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.Identifier.Text == "DefaultSchema")
            .ToList();
        schemas.Should().HaveCount(1, "a database has one schema, whichever engines persist its boundaries");

        var schema = Read(schemas[0].SyntaxTree.ToString());
        schema.Tables.Select(t => t.Name).Order(StringComparer.Ordinal).ToList().Should().Equal("Products", "Reviews");
        schema.ProviderName.Should().Be("PostgreSql");
    }

    /// <summary>Without EF Core the database still has its schema: the Sql engine's tables, typed for the driver the host references.</summary>
    [Fact]
    public void AHostOfTheSqlEngineAlone_HasTheSchemaOfItsDriver()
    {
        const string reviews = """
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Shop.Reviews { [Boundary] public partial class ReviewsBoundary; }

            namespace Shop.Reviews.Entities
            {
                [Entity] [BelongsTo<Shop.Reviews.ReviewsBoundary>]
                public partial class Review : IEntity { public string Text { get; set; } = ""; }
            }
            """;
        var output = Run([reviews, SqlServerDriver, SqlBridge, Host]);

        var schema = Read(Generated(output, "_Infra.Persistence.SchemaMetadata.g.cs"));
        schema.Tables.Select(t => t.Name).Should().Equal("Reviews");
        schema.ProviderName.Should().Be("SqlServer");
        schema.Tables[0].Columns.Single(c => c.Name == "PersistenceId").SqlType.Should().Be("uniqueidentifier", "the tables are typed for the host's driver");
    }

    /// <summary>
    ///     A host's boundaries are libraries it references, placed on a database by its <c>[Include&lt;TModule, TDatabase&gt;]</c>: their
    ///     entities' tables reach that database's schema, named after it, with its connection's key.
    /// </summary>
    [Fact]
    public void ABoundaryOfAReferencedLibrary_ReachesTheSchemaOfTheDatabaseTheHostPutsItOn()
    {
        const string library = """
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Shop.Reviews
            {
                [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)] public partial class ReviewsBoundary;
                [Module] public sealed class ReviewsModule;
            }

            namespace Shop.Reviews.Entities
            {
                [Entity] [BelongsTo<Shop.Reviews.ReviewsBoundary>]
                public partial class Review : IEntity { public string Text { get; set; } = ""; }
            }
            """;
        const string host = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Database;
            using Pragmatic.Composition.Enums;

            namespace Shop.Host
            {
                [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Reviews")]
                public sealed class ReviewsDatabase : PragmaticDatabase;

                [Module] [Include<Shop.Reviews.ReviewsModule, ReviewsDatabase>]
                public sealed class HostModule;

                public static class Program { public static void Main() { } }
            }
            """;
        var reviews = Library("Shop.Reviews", [library, SqlBridge]);
        var output = Run([host, PostgreSqlDriver, SqlBridge], [reviews]);

        var schema = Read(Generated(output, "_Infra.Persistence.SchemaMetadata.ReviewsDatabase.g.cs"), "ReviewsDatabaseSchema");
        schema.Tables.Select(t => t.Name).Should().Equal("Reviews");
        schema.DatabaseName.Should().Be("ReviewsDatabase");
        schema.ConfigKey.Should().Be("ConnectionStrings:Reviews");
    }

    private const string PostgreSqlDriver = "namespace Npgsql { public sealed class NpgsqlConnection { } }";

    private const string SqlServerDriver = "namespace Microsoft.Data.SqlClient { public sealed class SqlConnection { } }";

    private static readonly Type[] Runtime =
    [
        typeof(global::Pragmatic.Persistence.Entity.EntityAttribute), typeof(global::Pragmatic.Actions.Attributes.BoundaryAttribute),
        typeof(global::Pragmatic.Composition.Attributes.ModuleAttribute), typeof(global::Pragmatic.Persistence.Sql.SqlSession), typeof(SchemaVersion),
        // What a boundary's generated code reads: its specifications, and the values a specification's lambda captured.
        typeof(global::Pragmatic.Specification.Specification<>), typeof(global::Pragmatic.Persistence.Expressions.Captured),
    ];

    /// <summary><paramref name="sources" /> compiled as a host and run through the generator.</summary>
    private static Compilation Run(string[] sources, MetadataReference[]? libraries = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Pragmatic.Persistence.Sql.Generated")]);
        var compilation = CSharpCompilation.Create("SchemaProbe", sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)),
            References(Runtime).Concat(libraries ?? []),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        CSharpGeneratorDriver.Create(new PragmaticSourceGenerator()).WithUpdatedParseOptions(parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);
        return generated;
    }

    /// <summary><paramref name="sources" /> compiled as a library with what the generator writes into it, as a reference.</summary>
    private static MetadataReference Library(string name, string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Pragmatic.Persistence.Sql.Generated")]);
        var compilation = CSharpCompilation.Create(name, sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)), References(Runtime),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        CSharpGeneratorDriver.Create(new PragmaticSourceGenerator()).WithUpdatedParseOptions(parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);
        using var image = new MemoryStream();
        var emitted = generated.Emit(image);
        if (!emitted.Success)
            throw new InvalidOperationException($"The library {name} does not compile: " + string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static string Generated(Compilation output, string hint)
        => output.SyntaxTrees.SingleOrDefault(t => t.FilePath.EndsWith(hint, StringComparison.Ordinal))?.ToString()
           ?? throw new InvalidOperationException($"The generator emitted no {hint}: " + string.Join(", ", output.SyntaxTrees.Select(t => Path.GetFileName(t.FilePath))));

    /// <summary><c>{type}.Current</c> of <paramref name="source" />, compiled alone.</summary>
    private static SchemaVersion Read(string source, string type = "DefaultSchema")
    {
        var compilation = CSharpCompilation.Create("Schema" + Guid.NewGuid().ToString("N"), [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References([typeof(SchemaVersion)]),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
            throw new InvalidOperationException("The schema does not compile alone: " + string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var holder = Assembly.Load(image.ToArray()).GetTypes().Single(t => t.Name == type);
        return (SchemaVersion)holder.GetField("Current", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    }

    private static List<MetadataReference> References(IEnumerable<Type> runtime)
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll" or "Microsoft.Extensions.DependencyInjection.Abstractions.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Concat(runtime.Select(t => t.Assembly.Location).Distinct().Select(l => MetadataReference.CreateFromFile(l)))
            .ToList();
}
