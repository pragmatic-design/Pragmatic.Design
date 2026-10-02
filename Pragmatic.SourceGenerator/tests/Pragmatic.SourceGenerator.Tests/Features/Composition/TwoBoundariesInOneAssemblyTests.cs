using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator.Features.Composition.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     An assembly declaring two <c>[Boundary]</c> carries two <c>[PragmaticModuleMetadata]</c>, and
///     the host has to register both.
/// </summary>
/// <remarks>
///     The reader must not return at the first attribute it matches. Everything else about the second
///     boundary is still generated — its actions interface, its DbContext, its own metadata — so the
///     host would register none of it, silently; which of the two survived would be attribute order,
///     which no author chooses. The single-boundary case is the control: it must still read exactly one.
/// </remarks>
public class TwoBoundariesInOneAssemblyTests
{
    private const string AttributeDeclarations = """
        namespace Pragmatic.Actions.Metadata
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticModuleMetadataAttribute : System.Attribute
            {
                public System.Type? BoundaryType { get; set; }
            }
        }
        namespace Contoso.Billing
        {
            public sealed class InvoicingBoundary { }
            public sealed class LedgerBoundary { }
        }
        """;

    [Fact]
    public void TwoBoundaryMetadataAttributes_AreBothRead()
    {
        var modules = ReadModulesFrom("""
            [assembly: Pragmatic.Actions.Metadata.PragmaticModuleMetadata(
                BoundaryType = typeof(Contoso.Billing.InvoicingBoundary))]
            [assembly: Pragmatic.Actions.Metadata.PragmaticModuleMetadata(
                BoundaryType = typeof(Contoso.Billing.LedgerBoundary))]
            """);

        modules.Should().HaveCount(2,
            "the assembly declares two boundaries, and the host registers what it reads");
        modules.Select(m => m.Name).Should().BeEquivalentTo(["Invoicing", "Ledger"]);
    }

    [Fact]
    public void OneBoundaryMetadataAttribute_IsReadOnce()
    {
        var modules = ReadModulesFrom("""
            [assembly: Pragmatic.Actions.Metadata.PragmaticModuleMetadata(
                BoundaryType = typeof(Contoso.Billing.InvoicingBoundary))]
            """);

        modules.Should().HaveCount(1);
        modules[0].Name.Should().Be("Invoicing");
    }

    /// <summary>
    ///     Compiles <paramref name="assemblyAttributes" /> into a referenced assembly and reads the
    ///     domain modules a host compilation would see through that reference.
    /// </summary>
    private static IReadOnlyList<Pragmatic.SourceGenerator.Features.Composition.Models.DiscoveredModuleInfo>
        ReadModulesFrom(string assemblyAttributes)
    {
        var reference = CompileToReference("Contoso.Billing", assemblyAttributes + AttributeDeclarations);

        var host = CSharpCompilation.Create(
            "Contoso.Host",
            [CSharpSyntaxTree.ParseText("public class HostMarker { }", path: "Host.cs")],
            [.. RuntimeReferences(), reference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return MetadataReader.ReadDomainModulesFromReferences(host, CancellationToken.None);
    }

    private static MetadataReference[] RuntimeReferences()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "netstandard.dll"))
        ];
    }

    private static MetadataReference CompileToReference(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, path: $"{assemblyName}.cs")],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        emitResult.Success.Should().BeTrue(
            "the reference assembly must compile: {0}",
            string.Join(Environment.NewLine, emitResult.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
