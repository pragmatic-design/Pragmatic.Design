using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Migrations.Schema;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>LocalizedString</c> member of an entity the Sql engine persists is stored through <c>Pragmatic.Internationalization.Engines</c>'
///     converter, as the JSON text EF Core's <c>LocalizedStringValueConverter</c> writes. An assembly without that package is told so on the
///     property (PRAG3003, naming the package), as for a <c>ProtectedValue</c> (<see cref="AProtectedValueOnTheSqlEngineNeedsItsConverterTests" />).
/// </summary>
public class ALocalizedStringOnTheSqlEngineNeedsItsConverterTests
{
    private const string Host = "public static class LocalizedProbeHost { public static void Main() { } }";

    private const string SqlBridge = "namespace Pragmatic.Persistence.Sql { public static class PragmaticSqlFilters { } }";

    private const string Driver = "namespace Npgsql { public sealed class NpgsqlConnection { } }";

    /// <summary>The value type, as Pragmatic.Internationalization declares it: its ToString() override is what the column's store type is read from.</summary>
    private const string LocalizedString = "namespace Pragmatic.Internationalization.Types { public sealed class LocalizedString { public override string ToString() => \"\"; } }";

    /// <summary>The converter, as Pragmatic.Internationalization.Engines declares it.</summary>
    private const string Converter = """
        namespace Pragmatic.Internationalization.Engines.Sql
        {
            public sealed class SqlLocalizedString : Pragmatic.Persistence.Sql.Conversion.ISqlConverter<Pragmatic.Internationalization.Types.LocalizedString, string>
            {
                public static string ToStore(Pragmatic.Internationalization.Types.LocalizedString value) => "{}";
                public static Pragmatic.Internationalization.Types.LocalizedString FromStore(string value) => new();
            }
        }
        """;

    private const string Entities = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe;

        [Entity]
        public partial class Leaflet : IEntity
        {
            public Pragmatic.Internationalization.Types.LocalizedString Title { get; set; } = new();
        }

        // A read of the entity on the engine, as an application has one: what brings it into the engine's model.
        public static class Leaflets
        {
            public static System.Threading.Tasks.Task<long> CountAsync(Pragmatic.Persistence.Sql.SqlSession session) => session.Query<Leaflet>().CountAsync();
        }
        """;

    [Fact]
    public void WithoutTheEnginesPackage_TheLocalizedStringIsRefusedNamingIt()
    {
        var diagnostics = Run(withConverter: false);

        diagnostics.Should().Contain(d => d.Id == "PRAG3003" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Pragmatic.Internationalization.Engines", StringComparison.Ordinal),
            "the property is stored through the package's converter, and the assembly does not have it; reported: {0}",
            string.Join(" | ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
    }

    /// <summary>The control: with the converter, the same entity is not refused.</summary>
    [Fact]
    public void WithTheEnginesPackage_TheLocalizedStringIsAColumn()
    {
        Run(withConverter: true).Should().NotContain(d => d.Id == "PRAG3003");
    }

    private static ImmutableArray<Diagnostic> Run(bool withConverter)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Pragmatic.Persistence.Sql.Generated")]);
        string[] sources = withConverter ? [Entities, Host, SqlBridge, Driver, LocalizedString, Converter] : [Entities, Host, SqlBridge, Driver, LocalizedString];
        var compilation = CSharpCompilation.Create("LocalizedProbe", sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)),
            References([typeof(global::Pragmatic.Persistence.Sql.SqlSession), typeof(global::Pragmatic.Persistence.Sql.Conversion.ISqlConverter<,>),
                typeof(global::Pragmatic.Persistence.Entity.EntityAttribute), typeof(SchemaVersion)]),
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
