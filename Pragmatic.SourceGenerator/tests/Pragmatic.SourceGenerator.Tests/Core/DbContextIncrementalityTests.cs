using System.Collections;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     Incrementality regression for <c>DbContextFeature</c>. Unlike the other feature pipelines, the
///     three metadata reads that feed DbContext + schema generation (ReadAccess, database topology,
///     trait entities) are only reachable through the raw <c>Compilation</c>, and two of them read
///     assembly-level attributes of REFERENCED assemblies. So this test compiles a real boundary
///     assembly to a <see cref="MetadataReference" /> instead of stubbing types in source: with an
///     empty result each reader returns the shared empty singleton and stays cached by accident,
///     which would make the test vacuous.
/// </summary>
public class DbContextIncrementalityTests
{
    /// <summary>
    ///     A boundary library as the host sees it: assembly-level module metadata (boundary + read-access
    ///     types) and trait-entity metadata, plus the Composition attributes the topology reader binds to.
    /// </summary>
    private const string BoundaryAssemblySource = """
        [assembly: Pragmatic.Actions.Metadata.PragmaticModuleMetadata(
            BoundaryType = typeof(Contoso.Booking.BookingBoundary),
            ReadAccessTypes = new[] { typeof(Contoso.Booking.Guest) })]
        [assembly: Contoso.Meta.PragmaticMetadata(19, "TraitEntities",
            "[{\"TypeName\":\"ReservationComment\",\"Namespace\":\"Contoso.Booking\",\"BoundaryName\":\"Booking\",\"TraitKind\":\"Comment\"}]")]

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
        namespace Contoso.Meta
        {
            // Deliberately NOT in Pragmatic.Composition.Attributes: the trait reader matches on the
            // simple name only, and keeping it out of that namespace avoids flipping HasComposition.
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticMetadataAttribute : System.Attribute
            {
                public PragmaticMetadataAttribute(int category, string name, string payload) { }
            }
        }
        namespace Contoso.Booking
        {
            public sealed class BookingBoundary { }
            public sealed class BookingModule { }
            public sealed class Guest { }
        }
        """;

    /// <summary>The host: a [Module] with an [Include&lt;TModule, TDatabase&gt;] so the topology is non-empty.</summary>
    private const string HostSource = """
        namespace Contoso.Host
        {
            [Pragmatic.Composition.Attributes.PragmaticDatabase(ConfigKey = "ConnectionStrings:App")]
            public sealed class AppDatabase { }

            [Pragmatic.Composition.Attributes.Module]
            [Pragmatic.Composition.Attributes.Include<Contoso.Booking.BookingModule, AppDatabase>]
            public sealed class HostModule { }
        }
        """;

    [Fact]
    public void DbContext_AddingUnrelatedClass_KeepsMetadataReadsCached()
    {
        var boundaryReference = CompileToReference("Contoso.Booking", BoundaryAssemblySource);

        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            HostSource, IncrementalityAssert.UnrelatedAddition, boundaryReference);

        // Non-vacuity: each read must have found something. An empty dictionary/array is the shared
        // empty singleton, which compares equal by reference and would stay cached regardless.
        CountOf(result, TrackingNames.PersistenceReadAccess).Should().BeGreaterThan(0,
            "the referenced assembly declares [PragmaticModuleMetadata] with ReadAccessTypes");
        CountOf(result, TrackingNames.PersistenceTraitEntities).Should().BeGreaterThan(0,
            "the referenced assembly declares trait-entity metadata (category 19)");
        HasTopology(result).Should().BeTrue(
            "the host [Module] declares [Include<BookingModule, AppDatabase>]");

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.PersistenceReadAccess,
            TrackingNames.PersistenceDatabaseTopology,
            TrackingNames.PersistenceTraitEntities,
            TrackingNames.PersistenceDbContextInput);
    }

    private static object OutputValue(IncrementalRunResult result, string trackingName)
        => result.RunResult.TrackedSteps[trackingName].SelectMany(step => step.Outputs).First().Value;

    /// <summary>
    ///     Element count of a tracked step's value, read through the non-generic <see cref="IEnumerable" />
    ///     so the assertion holds whether the value is a raw immutable collection or an equatable wrapper.
    /// </summary>
    private static int CountOf(IncrementalRunResult result, string trackingName)
    {
        var count = 0;
        foreach (var _ in (IEnumerable)OutputValue(result, trackingName))
            count++;

        return count;
    }

    private static bool HasTopology(IncrementalRunResult result)
    {
        var value = OutputValue(result, TrackingNames.PersistenceDatabaseTopology);
        return (bool)value.GetType().GetProperty("HasTopology")!.GetValue(value)!;
    }

    /// <summary>Compiles <paramref name="source" /> to an in-memory assembly reference.</summary>
    private static MetadataReference CompileToReference(string assemblyName, string source)
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        MetadataReference[] references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "netstandard.dll")),
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
            "the boundary reference assembly must compile: {0}",
            string.Join(Environment.NewLine, emitResult.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
