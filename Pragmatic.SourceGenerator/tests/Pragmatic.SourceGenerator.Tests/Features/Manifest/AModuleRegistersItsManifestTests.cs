using System.Linq;
using Pragmatic.Actions.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Manifest;

/// <summary>
///     A module of endpoints registers its own manifest at load time, so the runtime OpenAPI enrichment
///     has something to read in a host without Composition.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Only the aggregated manifest of a Composition host registered itself. An application that
///         maps generated endpoints with <c>MapPragmaticEndpoints()</c> and nothing else had an empty
///         registry, and <c>AddPragmaticOpenApi()</c> enriched nothing.
///     </para>
///     <para>
///         ⚠️ Then a module registered only when it referenced <c>Pragmatic.Endpoints.OpenApi</c>, where
///         the registry lived — the package an application publishes a document with, not the one a
///         library of endpoints needs. The registry is in <c>Pragmatic.Endpoints</c> now, which every
///         assembly with generated endpoints references.
///     </para>
///     <para>
///         The module registers the compact text its assembly attribute carries — the very string a
///         Composition host copies into its aggregated manifest — so a reader seeing both can tell they
///         are one manifest.
///     </para>
/// </remarks>
public class AModuleRegistersItsManifestTests
{
    /// <summary>A library of endpoints: it references <c>Pragmatic.Endpoints</c> and nothing about OpenAPI.</summary>
    private const string Module = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace App.Notes;

        [DomainAction]
        [Endpoint(HttpVerb.Get, "api/notes")]
        public partial class ListNotesAction : DomainAction<string>
        {
            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success("notes"));
        }
        """;

    private static string ManifestFile()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Module,
            GeneratorTestHelper.FromType<CompositeActionAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Endpoints.Processors.IEndpointPreProcessor)),
            // The assembly attribute the manifest is carried in.
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Composition.Attributes.PragmaticMetadataAttribute)));

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "_Metadata.PragmaticManifest");
        generated.Should().NotBeNullOrEmpty("the module has an endpoint, so it has a manifest");

        GeneratorTestHelper.GetCompilationErrors(result)
            .Where(e => e.Location.SourceTree?.FilePath.Contains("_Metadata.PragmaticManifest") == true)
            .Select(e => e.ToString())
            .Should().BeEmpty("the registration is generated code, and must compile");

        return generated!;
    }

    [Fact]
    public void AModuleOfEndpoints_RegistersItsManifest_AtLoad()
    {
        var file = ManifestFile();

        file.Should().Contain("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        file.Should().Contain("global::Pragmatic.Endpoints.Manifest.ManifestRegistry.Register(CompactJson);");
    }

    /// <summary>
    ///     What it registers is exactly what its assembly attribute carries, which is what a Composition
    ///     host embeds: the reader recognises the two as one manifest by that text.
    /// </summary>
    [Fact]
    public void WhatItRegisters_IsTheTextAHostEmbeds()
    {
        var file = ManifestFile();

        ManifestTestHarness.ConstantValue(file, "CompactJson")
            .Should().Be(ManifestTestHarness.AssemblyAttributeJson(file));
    }
}
