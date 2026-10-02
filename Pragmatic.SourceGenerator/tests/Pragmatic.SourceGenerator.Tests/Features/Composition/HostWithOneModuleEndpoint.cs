using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     A host whose one module declares one endpoint, run through the generator in host mode.
/// </summary>
/// <remarks>
///     The module is an emitted assembly carrying the endpoint metadata the Endpoints generator writes,
///     so what is exercised is the host's own wiring of an endpoint it discovered, not the module's.
/// </remarks>
internal static class HostWithOneModuleEndpoint
{
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

    // The shape the Endpoints generator writes into a module assembly: one endpoint, no group.
    private const string EndpointsMetadata = """
        {"generator":"Pragmatic.Endpoints.SourceGenerator","registrationMethod":null,
        "data":{"hasAspVersioning":false,"endpointsCount":1,
        "endpoints":[{"type":"global::App.Library.ListBooksQuery","verb":"Get","route":"/api/books","group":null}],
        "groupsCount":0,"groups":[]}}
        """;

    private static MetadataReference? _contracts;

    // One contract assembly for the module and the host: a metadata name that is ambiguous across
    // references resolves to null, and would switch the features off.
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

    /// <summary>Runs the generator on the host, declared anonymous or not.</summary>
    public static SourceGenRunResult Run(bool anonymous)
        => GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Host(anonymous), Contracts, LibraryModule);

    /// <summary>The host's generated service and endpoint wiring.</summary>
    public static string ServicesOf(SourceGenRunResult result)
    {
        var services = GeneratorTestHelper.GetGeneratedSource(result, "Host.Services");
        services.Should().NotBeNull("a host generates its service and endpoint wiring");
        return services!;
    }

    /// <summary>The body of the generated <c>MapAllEndpoints</c>, from its signature on.</summary>
    public static string MapAllEndpointsOf(SourceGenRunResult result)
    {
        var services = ServicesOf(result);

        var start = services.IndexOf("MapAllEndpoints(this IEndpointRouteBuilder endpoints)", System.StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the host maps the endpoints it discovered");
        var body = services.Substring(start);
        body.Should().Contain("var root = endpoints.MapGroup(prefix);",
            "the module's endpoint was discovered, so the root group exists");
        return body;
    }

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
