using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator.Features.Composition.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     The host knows a module has a boundary registration to call by finding it in the module's assembly (#634).
/// </summary>
/// <remarks>
///     <para>
///         The host called <c>Add{Module}Boundary</c> for the assemblies whose metadata listed actions or mutations. Queries are in no
///         list it reads, so a module whose operations were all queries had the registration generated and never called. The
///         registration itself — <c>{Module}BoundaryExtensions</c> in the boundary's namespace — is what the call depends on, and the
///         reference already carries it.
///     </para>
///     <para>
///         The host-level effect (the call emitted, the interface resolved) is measured in
///         <c>Pragmatic.Composition.HostWiring.Tests</c>' <c>AModuleWithOnlyQueriesHasItsBoundaryRegisteredTests</c>, which compiles a
///         real module; this pins the reading the host's decision rests on.
///     </para>
/// </remarks>
public class ABoundaryRegistrationIsReadOffTheModuleTests
{
    private const string Metadata = """
        [assembly: Pragmatic.Actions.Metadata.PragmaticModuleMetadata(BoundaryType = typeof(App.Workforce.WorkforceBoundary))]

        namespace Pragmatic.Actions.Metadata
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticModuleMetadataAttribute : System.Attribute
            {
                public System.Type? BoundaryType { get; set; }
            }
        }

        namespace App.Workforce
        {
            public sealed class WorkforceBoundary { }
        }
        """;

    private const string Registration = """

        namespace App.Workforce
        {
            public static class WorkforceBoundaryExtensions { }
        }
        """;

    [Fact]
    public void AModuleWhoseAssemblyHasTheRegistration_IsReadAsHavingOne()
        => ReadModuleFrom(Metadata + Registration).HasBoundaryRegistration.Should().BeTrue(
            "the module's generator wrote Add{Module}Boundary, whatever kind its operations are");

    /// <summary>The control: a boundary with no operations has no registration, and the host must not name one.</summary>
    [Fact]
    public void AModuleWhoseAssemblyHasNoRegistration_IsReadAsHavingNone()
        => ReadModuleFrom(Metadata).HasBoundaryRegistration.Should().BeFalse(
            "a host calling a registration nobody generated would not compile");

    private static Pragmatic.SourceGenerator.Features.Composition.Models.DiscoveredModuleInfo ReadModuleFrom(string source)
    {
        var reference = CompileToReference("App.Workforce", source);

        var host = CSharpCompilation.Create(
            "App.Host",
            [CSharpSyntaxTree.ParseText("public class HostMarker { }", path: "Host.cs")],
            [.. RuntimeReferences(), reference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var modules = MetadataReader.ReadDomainModulesFromReferences(host, CancellationToken.None);
        modules.Should().HaveCount(1);
        return modules[0];
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
