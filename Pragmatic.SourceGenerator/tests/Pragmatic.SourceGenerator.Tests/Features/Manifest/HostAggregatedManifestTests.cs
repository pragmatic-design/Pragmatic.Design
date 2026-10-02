using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Manifest;

/// <summary>
///     Host-side half of the manifest pipeline (<c>HostModeGenerator.Manifest</c>): the host reads the
///     <c>[PragmaticMetadata(Manifest, …)]</c> attribute of every referenced module, merges the module
///     manifests into <c>PragmaticManifest.Json</c>, derives the compile-time OpenAPI document, and —
///     only when the OpenApi runtime package is present — registers both through a
///     <c>[ModuleInitializer]</c>. None of that was exercised: it needs a real referenced assembly, so
///     the tests build one in memory.
/// </summary>
public class HostAggregatedManifestTests
{
    /// <summary>
    ///     Contract surface a boundary library exposes to the host. Declaring it in the module assembly
    ///     (rather than in the host source) reproduces the real topology: the host sees these types only
    ///     through a reference.
    /// </summary>
    private const string CompositionShims = """
        namespace Pragmatic.Composition.Metadata
        {
            public enum MetadataCategory
            {
                DI = 0, Mapping = 1, Actions = 2, Startup = 3, Validation = 4, Endpoints = 5,
                HealthChecks = 6, Identifiers = 7, Module = 8, Caching = 9, Persistence = 10,
                EventHandlers = 11, Configuration = 12, HostTopology = 13, Authorization = 14,
                MessageHandlers = 15, Jobs = 16, Sagas = 17, Manifest = 18
            }
        }
        namespace Pragmatic.Composition.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticMetadataAttribute : System.Attribute
            {
                public PragmaticMetadataAttribute(
                    Pragmatic.Composition.Metadata.MetadataCategory category, string schemaVersion, string jsonData)
                {
                    Category = category;
                    SchemaVersion = schemaVersion;
                    JsonData = jsonData;
                }

                public Pragmatic.Composition.Metadata.MetadataCategory Category { get; }
                public string SchemaVersion { get; }
                public string JsonData { get; }
            }
        }
        namespace Pragmatic.Composition.Hosting
        {
            // Presence of this type is what makes CompositionDetector treat the exe as a HOST.
            public class PragmaticBuilder { }
        }
        """;

    private const string ManifestTemplate = """
        {"$schema":"pragmatic-manifest/v1","version":"1.0.0","assembly":"@ASSEMBLY@","boundaries":[],
        "endpoints":[{"operationId":"@OPERATION@","httpMethod":"GET","fullRoute":"@ROUTE@",
        "successStatusCode":200,"isVoid":false,"response":{"type":"global::App.GuestDto"}}],
        "types":[{"type":"global::App.GuestDto","simpleName":"GuestDto","kind":"dto",
        "properties":[{"name":"Id","type":"System.Guid","isRequired":true,"isNullable":false}]}],
        "actions":[],"permissions":[]}
        """;

    private static string ModuleManifest(string assembly, string operationId, string route)
        => ManifestTemplate
            .Replace("@ASSEMBLY@", assembly)
            .Replace("@OPERATION@", operationId)
            .Replace("@ROUTE@", route)
            .Replace("\r", "")
            .Replace("\n", "");

    private static MetadataReference? _contracts;

    /// <summary>
    ///     The contract assembly every module and the host reference. It has to be ONE assembly:
    ///     <c>GetTypeByMetadataName</c> returns null when a metadata name is ambiguous across references,
    ///     so duplicating the shims per module would silently switch Composition off.
    /// </summary>
    private static MetadataReference Contracts => _contracts ??= Emit("Pragmatic.Composition", CompositionShims);

    /// <summary>Compiles a boundary-library assembly carrying the given manifest payloads.</summary>
    private static MetadataReference ModuleAssembly(string assemblyName, params string[] manifestJsons)
    {
        var attributes = string.Join(Environment.NewLine, manifestJsons.Select(json =>
            "[assembly: Pragmatic.Composition.Attributes.PragmaticMetadata("
            + "Pragmatic.Composition.Metadata.MetadataCategory.Manifest, \"1.0.0\", "
            + SymbolDisplay.FormatLiteral(json, quote: true) + ")]"));

        return Emit(assemblyName, attributes, Contracts);
    }

    private static MetadataReference Emit(string assemblyName, string source, params MetadataReference[] extra)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            BaseReferences.Concat(extra),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join("; ",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static MetadataReference[] BaseReferences =>
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
    ];

    private const string HostSource = """
        namespace MyHost
        {
            public static class Program { public static void Main() { } }
        }
        """;

    private static (SourceGenRunResult Result, Dictionary<string, string> Sources) RunHost(
        string hostSource, params MetadataReference[] modules)
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            hostSource, [Contracts, .. modules]);
        return (result, GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public void Host_MergesEveryReferencedModuleManifest_AndDerivesOpenApi()
    {
        var (_, generated) = RunHost(HostSource,
            ModuleAssembly("App.Booking", ModuleManifest("App.Booking", "Booking.GetGuest", "/booking/guests/{id}")),
            ModuleAssembly("App.Billing", ModuleManifest("App.Billing", "Billing.GetInvoice", "/billing/invoices/{id}")));

        generated.Should().ContainKey("_Metadata.PragmaticManifest.Aggregated.g.cs");
        generated.Should().ContainKey("_Infra.OpenApi.Generated.g.cs");

        var aggregated = ManifestTestHarness.ConstantValue(
            generated["_Metadata.PragmaticManifest.Aggregated.g.cs"], "Json");
        JsonValidator.IsValid(aggregated).Should().BeTrue();

        using var doc = JsonDocument.Parse(aggregated);
        doc.RootElement.GetProperty("aggregated").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("assembly").GetString().Should().Be("TestAssembly");
        doc.RootElement.GetProperty("moduleCount").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("modules").EnumerateArray()
            .Select(m => m.GetProperty("assembly").GetString())
            .Should().BeEquivalentTo("App.Booking", "App.Billing");

        generated["_Metadata.PragmaticManifest.Aggregated.g.cs"].Should().Contain("internal const int ModuleCount = 2;");

        var openApi = ManifestTestHarness.ConstantValue(generated["_Infra.OpenApi.Generated.g.cs"], "Json");
        JsonValidator.IsValid(openApi).Should().BeTrue();

        using var openApiDoc = JsonDocument.Parse(openApi);
        openApiDoc.RootElement.GetProperty("openapi").GetString().Should().Be("3.1.0");
        openApiDoc.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("/booking/guests/{id}", "/billing/invoices/{id}");
    }

    [Fact]
    public void Host_WithAnUnreadableModuleManifest_StillComposes()
    {
        // The manifest of a referenced module is produced by another compilation, possibly by an
        // older generator, and is embedded verbatim. MetadataJsonBuilder rejects invalid JSON; if that
        // rejection travelled up as PRAG9000 it would abort GenerateHost — which emits the aggregated
        // manifest BEFORE the host wiring, so a purely informational artifact would take the whole
        // composition with it. The bad module is dropped, the good one survives, and the host is
        // still generated.
        var (result, generated) = RunHost(HostSource,
            ModuleAssembly("App.Booking", ModuleManifest("App.Booking", "Booking.GetGuest", "/booking/guests/{id}")),
            ModuleAssembly("App.Broken", """{ "assembly": "App.Broken", "endpoints": [ }"""));

        var aggregated = ManifestTestHarness.ConstantValue(
            generated["_Metadata.PragmaticManifest.Aggregated.g.cs"], "Json");
        JsonValidator.IsValid(aggregated).Should().BeTrue();

        using var doc = JsonDocument.Parse(aggregated);
        doc.RootElement.GetProperty("moduleCount").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("modules").EnumerateArray()
            .Select(m => m.GetProperty("assembly").GetString())
            .Should().BeEquivalentTo("App.Booking");

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG1613").Should().ContainSingle()
            .Which.GetMessage().Should().Contain("App.Broken");

        // The failure must not be dressed up as a generator crash, and the host wiring — which is
        // emitted after this point and was the real casualty — must be there.
        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG9000").Should().BeEmpty();
        generated.Should().ContainKey("Host.Services.g.cs");
        generated.Should().ContainKey("Host.Entry.g.cs");
    }

    [Fact]
    public void Host_WithoutTheOpenApiRuntime_EmitsNoModuleInitializer()
    {
        // This host references neither Pragmatic.Endpoints nor Pragmatic.Endpoints.OpenApi, so neither
        // registry exists: emitting the initializer would not compile, so the constants are generated
        // but nothing self-registers.
        var (_, generated) = RunHost(HostSource, ModuleAssembly("App.Booking", ModuleManifest("App.Booking", "Booking.Get", "/g")));

        generated["_Metadata.PragmaticManifest.Aggregated.g.cs"].Should().NotContain("ModuleInitializer");
        generated["_Infra.OpenApi.Generated.g.cs"].Should().NotContain("ModuleInitializer");
    }

    private const string HostWithRegistries = """
        namespace Pragmatic.Endpoints.Manifest
        {
            public static class ManifestRegistry { public static void Register(string json) { } }
        }
        namespace Pragmatic.Endpoints.OpenApi
        {
            // The signature mirrors the real one: the generated code also passes whether the operations
            // require authentication, because the document does not say it and the wiring must tell
            // «no authentication» from «authentication that was not described».
            public static class PragmaticOpenApiRegistry
            {
                public static void Register(string json, bool requiresAuthentication = false) { }
            }
        }
        namespace MyHost
        {
            public static class Program { public static void Main() { } }
        }
        """;

    [Fact]
    public void Host_WithTheOpenApiRuntime_RegistersManifestAndOpenApiAtModuleLoad()
    {
        var (_, generated) = RunHost(HostWithRegistries,
            ModuleAssembly("App.Booking", ModuleManifest("App.Booking", "Booking.Get", "/g")));

        var manifestFile = generated["_Metadata.PragmaticManifest.Aggregated.g.cs"];
        manifestFile.Should().Contain("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        manifestFile.Should().Contain("global::Pragmatic.Endpoints.Manifest.ManifestRegistry.Register(Json);");

        var openApiFile = generated["_Infra.OpenApi.Generated.g.cs"];
        openApiFile.Should().Contain("[ModuleInitializer]");
        openApiFile.Should().Contain(
            "global::Pragmatic.Endpoints.OpenApi.PragmaticOpenApiRegistry.Register(");
        openApiFile.Should().Contain("requiresAuthentication:",
            "the wiring needs to know whether anything requires authentication: the document says "
            + "which operations opt out, not whether at least one remains that does not");

        // Both files must be valid C# on their own — a ModuleInitializer that does not compile takes
        // the whole host build down.
        foreach (var file in new[] { manifestFile, openApiFile })
        {
            CSharpSyntaxTree.ParseText(file).GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        }
    }

    /// <summary>
    ///     The host registers its own document in its own container, and the flag travels
    ///     with it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The <c>[ModuleInitializer]</c> above stays, as the fallback for a host that registers
    ///         nothing, but it writes a <b>static of the process</b>: with two hosts in one process the
    ///         one loaded second answered for both, and a service's <c>/openapi/v1.json</c> returned the
    ///         other service's document. The registration below is what makes the answer belong to the
    ///         host that was asked.
    ///     </para>
    ///     <para>
    ///         ⚠️ Both values come from <c>PragmaticOpenApi</c>, this host's own generated class — the
    ///         flag included. Reading the flag from <c>PragmaticOpenApiRegistry</c> would have published
    ///         one host's security requirement on the other's contract, which is the half of this defect
    ///         that ships a <em>wrong</em> document rather than an empty one.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Host_WithTheOpenApiRuntime_RegistersItsOwnDocumentInItsOwnContainer()
    {
        var (_, generated) = RunHost(HostWithRegistries,
            ModuleAssembly("App.Booking", ModuleManifest("App.Booking", "Booking.Get", "/g")));

        var services = generated["Host.Services.g.cs"];

        services.Should().Contain(
            "services.AddSingleton(new global::Pragmatic.Endpoints.OpenApi.HostOpenApiDocument("
            + "PragmaticOpenApi.Json, PragmaticOpenApi.RequiresAuthentication));",
            "the document and its flag are facts about this host, so they come from this host's own "
            + "generated constants and live in this host's container");

        generated["_Infra.OpenApi.Generated.g.cs"].Should().Contain(
            "internal const bool RequiresAuthentication =",
            "which is the constant that line names: without it the flag could only come from the "
            + "process-wide registry");
    }

    /// <summary>
    ///     Control — a host that cannot serve the document registers nothing, because
    ///     <c>HostOpenApiDocument</c> is a type it does not reference.
    /// </summary>
    [Fact]
    public void Host_WithoutTheOpenApiRuntime_RegistersNoDocument()
    {
        var (_, generated) = RunHost(HostSource,
            ModuleAssembly("App.Booking", ModuleManifest("App.Booking", "Booking.Get", "/g")));

        generated["Host.Services.g.cs"].Should().NotContain("HostOpenApiDocument",
            "a registration naming a type the host cannot see would not compile");
    }

    [Fact]
    public void Host_NoModuleCarriesAManifest_EmitsNeitherArtifact()
    {
        var (_, generated) = RunHost(HostSource, ModuleAssembly("App.Empty"));

        generated.Should().NotContainKey("_Metadata.PragmaticManifest.Aggregated.g.cs");
        generated.Should().NotContainKey("_Infra.OpenApi.Generated.g.cs");
    }

    /// <summary>
    ///     A host that can serve its contract serves one before its first endpoint.
    /// </summary>
    /// <remarks>
    ///     An application with no operations has a contract: none. Emitting nothing sent the mount to
    ///     the branch written for a generator that failed, and every scaffold met a 404 saying so.
    /// </remarks>
    [Fact]
    public void Host_WithTheOpenApiRuntime_AndNoEndpointYet_PublishesADocumentWithNoOperations()
    {
        var (_, generated) = RunHost(HostWithRegistries, ModuleAssembly("App.Empty"));

        // ⚠️ The manifest exists even with no module to aggregate. A host with the registry registers
        // HostManifest(PragmaticManifest.Json), so its absence would be a CS0103 in every skeleton: the
        // manifest exists, with no module in it.
        generated.Should().ContainKey("_Metadata.PragmaticManifest.Aggregated.g.cs");
        using (var manifest = JsonDocument.Parse(ManifestTestHarness.ConstantValue(
                   generated["_Metadata.PragmaticManifest.Aggregated.g.cs"], "Json")))
        {
            manifest.RootElement.GetProperty("moduleCount").GetInt32().Should().Be(0);
            manifest.RootElement.GetProperty("modules").EnumerateArray().Should().BeEmpty();
        }

        generated.Should().ContainKey("_Infra.OpenApi.Generated.g.cs");

        var openApiFile = generated["_Infra.OpenApi.Generated.g.cs"];
        openApiFile.Should().Contain("global::Pragmatic.Endpoints.OpenApi.PragmaticOpenApiRegistry.Register(");

        var openApi = ManifestTestHarness.ConstantValue(openApiFile, "Json");
        using var document = JsonDocument.Parse(openApi);
        document.RootElement.GetProperty("openapi").GetString().Should().Be("3.1.0");
        document.RootElement.GetProperty("info").GetProperty("title").GetString().Should().Be("TestAssembly");
        document.RootElement.GetProperty("paths").EnumerateObject().Should().BeEmpty();
    }
}
