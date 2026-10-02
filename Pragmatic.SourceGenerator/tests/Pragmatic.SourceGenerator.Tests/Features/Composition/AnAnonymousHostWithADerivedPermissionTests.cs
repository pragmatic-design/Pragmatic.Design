using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     A host that declares <c>[AnonymousHost]</c> and discovers a route enforcing a <b>derived</b>
///     permission is told so: that route cannot be called by anyone, ever.
/// </summary>
/// <remarks>
///     <para>
///         The fact is reported on the host, where it has an answer, and not on the <b>module</b>
///         ("this compilation has no Pragmatic.Identity.AspNetCore, so if the host does not
///         authenticate the route answers 500"). A boundary library cannot know whether the host
///         authenticates — the module declares, the host composes — and that common case is already an
///         error on the host, <c>PRAG1695</c>, which refuses a host that authorizes nothing and has no
///         authentication at all.
///     </para>
///     <para>
///         What PRAG1695 does <b>not</b> cover is this one: a host that has declared it wants no
///         authentication, together with a route whose permission the author never asked for and cannot
///         switch off — <c>[Autocomplete]</c> derives it from the boundary and the entity. That route
///         answers 403 to every caller for the life of the application, and nothing else says so.
///     </para>
///     <para>
///         ⚠️ A module-level check would also pin a package: removing <c>Identity.AspNetCore</c> from
///         <c>Showcase.Catalog</c> — which needs nothing from it — would fail the build once per
///         <c>[Autocomplete]</c>.
///     </para>
/// </remarks>
public class AnAnonymousHostWithADerivedPermissionTests
{
    [Fact]
    public void AnAnonymousHost_WithARouteThatDerivesAPermission_IsTold()
    {
        var result = Run(anonymous: true);

        var diagnostic = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG1692")
            .Should().ContainSingle().Which;

        diagnostic.GetMessage().Should().Contain("catalog.amenity.read",
            "the message names the permission nobody can hold");
        diagnostic.GetMessage().Should().Contain("/api/amenities/autocomplete",
            "and the route it makes uncallable");
    }

    /// <summary>
    ///     The control: the same module under a host that <b>does</b> authenticate says nothing. The
    ///     route works there, which is the ordinary case and the one a module-level check would complain about.
    /// </summary>
    [Fact]
    public void AHostThatAuthenticates_IsNotTold()
        => GeneratorTestHelper.GetDiagnosticsById(Run(anonymous: false), "PRAG1692").Should().BeEmpty();

    /// <summary>
    ///     The second control: an anonymous host whose routes derive nothing is not warned either. The
    ///     diagnostic is about a permission the author cannot remove, not about anonymity.
    /// </summary>
    [Fact]
    public void AnAnonymousHost_WithNoDerivedPermission_IsNotTold()
        => GeneratorTestHelper
            .GetDiagnosticsById(HostWithOneModuleEndpoint.Run(anonymous: true), "PRAG1692")
            .Should().BeEmpty();

    /// <summary>PRAG0519 is retired and not reused: nothing emits it.</summary>
    [Fact]
    public void TheModuleSideDiagnostic_IsRetired()
    {
        GeneratorTestHelper.GetDiagnosticsById(Run(anonymous: true), "PRAG0519").Should().BeEmpty();
        GeneratorTestHelper.GetDiagnosticsById(Run(anonymous: false), "PRAG0519").Should().BeEmpty();
    }

    // ── The harness: a module whose metadata declares an autocomplete route, as the Endpoints
    //    generator writes it, and a host that discovers it.

    private const string ContractShims = """
        namespace Pragmatic.Composition.Metadata
        {
            public enum MetadataCategory
            {
                DI = 0, Mapping = 1, Actions = 2, Startup = 3, Validation = 4, Endpoints = 5
            }
        }
        namespace Pragmatic.Composition.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticMetadataAttribute : System.Attribute
            {
                public PragmaticMetadataAttribute(
                    Pragmatic.Composition.Metadata.MetadataCategory category, string schemaVersion, string jsonData) { }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
            public sealed class ModuleAttribute : System.Attribute
            {
                public string? Name { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
            public sealed class AnonymousHostAttribute : System.Attribute { }
        }
        namespace Pragmatic.Composition.Hosting { public class PragmaticBuilder { } }
        namespace Pragmatic.Authorization { public static class PragmaticBuilderAuthorizationExtensions { } }
        namespace Pragmatic.Endpoints.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EndpointAttribute : System.Attribute { }
        }
        """;

    /// <summary>
    ///     One autocomplete route, with the permission it derived — the shape the Endpoints generator
    ///     writes for a <c>[Autocomplete]</c> property.
    /// </summary>
    private const string EndpointsMetadata = """
        {"generator":"Pragmatic.Endpoints.SourceGenerator","registrationMethod":null,
        "data":{"hasAspVersioning":false,"endpointsCount":1,
        "endpoints":[{"type":"global::App.Library.AmenityAutocompleteEndpoint","verb":"Get",
        "route":"/api/amenities/autocomplete","group":null,"derivedPermission":"catalog.amenity.read"}],
        "groupsCount":0,"groups":[]}}
        """;

    private static MetadataReference? _contracts;

    private static MetadataReference Contracts => _contracts ??= Emit("Pragmatic.Composition", ContractShims);

    private static MetadataReference LibraryModule => Emit("App.Library",
        "[assembly: Pragmatic.Composition.Attributes.PragmaticMetadata("
        + "Pragmatic.Composition.Metadata.MetadataCategory.Endpoints, \"1.0.0\", "
        + SymbolDisplay.FormatLiteral(EndpointsMetadata.Replace("\r", "").Replace("\n", ""), quote: true) + ")]",
        Contracts);

    private static string Host(bool anonymous) => $$"""
        namespace App.Host
        {
            using Pragmatic.Composition.Attributes;

            [Module(Name = "AppHost")]
            {{(anonymous ? "[AnonymousHost]" : "")}}
            public sealed class HostModule { }

            public static class Program { public static void Main() { } }
        }
        """;

    private static SourceGenRunResult Run(bool anonymous)
        => GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Host(anonymous), Contracts, LibraryModule);

    private static MetadataReference Emit(string assemblyName, string source, params MetadataReference[] extra)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(
                    Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
                .. extra
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join("; ",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
