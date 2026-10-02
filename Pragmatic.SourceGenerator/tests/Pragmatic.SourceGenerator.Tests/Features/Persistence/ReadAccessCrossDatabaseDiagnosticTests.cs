using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     PRAG0706 — a <c>[ReadAccess&lt;TEntity&gt;]</c> whose target is owned by a boundary the host maps to a
///     different database. <c>DbContextFeature</c> still emits the <c>DbSet</c> on the reading boundary's
///     DbContext and excludes it from that database's migrations, so the table exists only in the owning
///     database: the join compiles and dies at runtime on a missing table.
///     <para>
///     Runs the real generator in host mode against a compiled boundary reference: the boundary→database
///     mapping is only reachable through assembly-level <c>[PragmaticModuleMetadata]</c> of REFERENCED
///     assemblies, so stubbing the types in host source would leave the topology empty and the test vacuous.
///     </para>
/// </summary>
public class ReadAccessCrossDatabaseDiagnosticTests
{
    /// <summary>
    ///     The attribute surface, in its own assembly. Both domain assemblies and the host must see the
    ///     SAME attribute symbols: declared twice, <c>GetTypeByMetadataName</c> resolves one of the two
    ///     and the metadata of the other assembly stops matching.
    /// </summary>
    private const string AbstractionsSource = """
        namespace Pragmatic.Actions.Metadata
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticModuleMetadataAttribute : System.Attribute
            {
                public System.Type? BoundaryType { get; set; }
                public System.Type[]? ReadAccessTypes { get; set; }
            }
        }
        namespace Pragmatic.Composition.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ModuleAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class IncludeAttribute<TModule, TDatabase> : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticDatabaseAttribute : System.Attribute
            {
                public string? ConfigKey { get; set; }
            }
        }
        namespace Pragmatic.Persistence.EFCore
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : System.Attribute { }
        }
        namespace Pragmatic.Persistence.Entity
        {
            public sealed class EntityAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class BelongsToAttribute<TBoundary> : System.Attribute { }
        }
        """;

    /// <summary>Catalog owns Product. One module, one boundary, its own assembly.</summary>
    private const string CatalogAssemblySource = """
        [assembly: Pragmatic.Actions.Metadata.PragmaticModuleMetadata(
            BoundaryType = typeof(Contoso.Catalog.CatalogBoundary))]

        namespace Contoso.Catalog
        {
            public sealed class CatalogBoundary { }
            public sealed class CatalogModule { }

            [Pragmatic.Persistence.Entity.Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<CatalogBoundary>]
            public class Product { public string Code { get; set; } = ""; }
        }
        """;

    /// <summary>
    ///     Booking reads Catalog's Product. Mirrors Showcase's Booking → Catalog read access, and it
    ///     is a separate assembly because a module is one per assembly: that is what puts the two on
    ///     different databases in the first place.
    /// </summary>
    private const string BookingAssemblySource = """
        [assembly: Pragmatic.Actions.Metadata.PragmaticModuleMetadata(
            BoundaryType = typeof(Contoso.Booking.BookingBoundary),
            ReadAccessTypes = new[] { typeof(Contoso.Catalog.Product) })]

        namespace Contoso.Booking
        {
            public sealed class BookingBoundary { }
            public sealed class BookingModule { }

            [Pragmatic.Persistence.Entity.Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<BookingBoundary>]
            public class Reservation { public string Reference { get; set; } = ""; }
        }
        """;

    /// <summary>The host topology. <paramref name="catalogDatabase"/> is what the test varies.</summary>
    private static string HostSource(string catalogDatabase) => $$"""
        namespace Contoso.Host
        {
            [Pragmatic.Composition.Attributes.PragmaticDatabase(ConfigKey = "ConnectionStrings:App")]
            public sealed class AppDatabase { }

            [Pragmatic.Composition.Attributes.PragmaticDatabase(ConfigKey = "ConnectionStrings:Financial")]
            public sealed class FinancialDatabase { }

            [Pragmatic.Composition.Attributes.Module]
            [Pragmatic.Composition.Attributes.Include<Contoso.Booking.BookingModule, AppDatabase>]
            [Pragmatic.Composition.Attributes.Include<Contoso.Catalog.CatalogModule, {{catalogDatabase}}>]
            public sealed class HostModule { }

            public static class Program { public static void Main() { } }
        }
        """;

    private static SourceGenRunResult RunHost(string catalogDatabase)
    {
        var abstractions = CompileToReference("Contoso.Abstractions", AbstractionsSource);
        var catalog = CompileToReference("Contoso.Catalog", CatalogAssemblySource, abstractions);
        var booking = CompileToReference("Contoso.Booking", BookingAssemblySource, abstractions, catalog);

        return GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            HostSource(catalogDatabase), abstractions, catalog, booking);
    }

    /// <summary>
    ///     The dangerous topology: Booking (AppDatabase) reads Catalog's Product, which lives on
    ///     FinancialDatabase. The generated BookingDbContext queries a table its connection cannot see.
    /// </summary>
    [Fact]
    public void ReadAccessAcrossDatabases_ReportsPRAG0706()
    {
        var result = RunHost("FinancialDatabase");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0706").Should().BeTrue(
            "Booking is on AppDatabase and Product's owner Catalog is on FinancialDatabase");

        var diagnostic = GeneratorTestHelper
            .GetGeneratorDiagnostics(result, "PRAG0706")
            .Should().ContainSingle().Subject;

        diagnostic.GetMessage().Should()
            .Contain("BookingBoundary").And.Contain("Product")
            .And.Contain("AppDatabase").And.Contain("FinancialDatabase");
        diagnostic.Location.Should().NotBe(Location.None,
            "the diagnostic must point at the host [Include] that created the split");
        diagnostic.Location.GetLineSpan().Path.Should().Be("TestSource.cs");
    }

    /// <summary>
    ///     The correct case next door: the very same [ReadAccess] declaration, with both modules on one
    ///     database. This is the Showcase topology (Booking + Catalog both on ShowcaseAppDatabase).
    /// </summary>
    [Fact]
    public void ReadAccessWithinOneDatabase_IsSilent()
    {
        var result = RunHost("AppDatabase");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0706").Should().BeFalse(
            "[ReadAccess] is a same-database SQL join and both modules are on AppDatabase");
    }

    [Fact]
    public void Descriptor_IsAWarningInThePersistenceRange()
    {
        ReadAccessDiagnostics.ReadAccessCrossDatabase.Id.Should().Be("PRAG0706");
        ReadAccessDiagnostics.ReadAccessCrossDatabase.DefaultSeverity
            .Should().Be(DiagnosticSeverity.Warning);
    }

    /// <summary>Compiles <paramref name="source"/> to an in-memory assembly reference.</summary>
    private static MetadataReference CompileToReference(
        string assemblyName, string source, params MetadataReference[] dependencies)
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        MetadataReference[] references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "netstandard.dll")),
            .. dependencies
        ];

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, path: $"{assemblyName}.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        emitResult.Success.Should().BeTrue(
            "the reference assembly {1} must compile: {0}",
            string.Join(Environment.NewLine, emitResult.Diagnostics), assemblyName);

        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
