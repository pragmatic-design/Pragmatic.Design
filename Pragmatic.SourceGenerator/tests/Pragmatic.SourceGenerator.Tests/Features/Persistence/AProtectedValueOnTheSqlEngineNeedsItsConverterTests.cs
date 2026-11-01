using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Migrations.Schema;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>ProtectedValue</c> member of an entity the Sql engine persists is stored through <c>Pragmatic.Cryptography.Engines</c>' converter. An
///     assembly without that package is told so on the property (PRAG3003, naming the package) instead of failing on a type missing from a
///     generated file, as PRAG0652 tells it for EF Core.
/// </summary>
public class AProtectedValueOnTheSqlEngineNeedsItsConverterTests
{
    private const string Host = "public static class ProtectedProbeHost { public static void Main() { } }";

    private const string SqlBridge = "namespace Pragmatic.Persistence.Sql { public static class PragmaticSqlFilters { } }";

    private const string Driver = "namespace Npgsql { public sealed class NpgsqlConnection { } }";

    /// <summary>The value type, as Pragmatic.Cryptography declares it.</summary>
    private const string ProtectedValue = "namespace Pragmatic.Cryptography { public readonly record struct ProtectedValue(byte[] Packed); }";

    /// <summary>The converter, as Pragmatic.Cryptography.Engines declares it.</summary>
    private const string Converter = """
        namespace Pragmatic.Cryptography.Engines.Sql
        {
            public sealed class SqlProtectedValue : Pragmatic.Persistence.Sql.Conversion.ISqlConverter<Pragmatic.Cryptography.ProtectedValue, byte[]>
            {
                public static byte[] ToStore(Pragmatic.Cryptography.ProtectedValue value) => value.Packed;
                public static Pragmatic.Cryptography.ProtectedValue FromStore(byte[] value) => new(value);
            }
        }
        """;

    private const string Entities = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe;

        [Entity]
        public partial class Dossier : IEntity
        {
            public Pragmatic.Cryptography.ProtectedValue Note { get; set; }
        }

        // A read of the entity on the engine, as an application has one: what brings it into the engine's model.
        public static class Dossiers
        {
            public static System.Threading.Tasks.Task<long> CountAsync(Pragmatic.Persistence.Sql.SqlSession session) => session.Query<Dossier>().CountAsync();
        }
        """;

    [Fact]
    public void WithoutTheEnginesPackage_TheProtectedValueIsRefusedNamingIt()
    {
        var diagnostics = Run(withConverter: false);

        diagnostics.Should().Contain(d => d.Id == "PRAG3003" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Pragmatic.Cryptography.Engines", StringComparison.Ordinal),
            "the property is stored through the package's converter, and the assembly does not have it");
    }

    /// <summary>The control: with the converter, the same entity is not refused.</summary>
    [Fact]
    public void WithTheEnginesPackage_TheProtectedValueIsAColumn()
    {
        Run(withConverter: true).Should().NotContain(d => d.Id == "PRAG3003");
    }

    private static ImmutableArray<Diagnostic> Run(bool withConverter)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Pragmatic.Persistence.Sql.Generated")]);
        string[] sources = withConverter ? [Entities, Host, SqlBridge, Driver, ProtectedValue, Converter] : [Entities, Host, SqlBridge, Driver, ProtectedValue];
        var compilation = CSharpCompilation.Create("ProtectedProbe", sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)),
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
