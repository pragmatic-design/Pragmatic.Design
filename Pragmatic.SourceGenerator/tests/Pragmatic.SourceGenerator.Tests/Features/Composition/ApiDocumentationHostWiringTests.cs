using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     A host that references Pragmatic.Endpoints.OpenApi publishes the compile-time document from its
///     generated entry point, and names Scalar only when it references Scalar.
/// </summary>
/// <remarks>
///     Marker types are stubbed so FeatureDetector triggers without the runtime packages; the generated
///     entry references types absent from the stub compilation, so assertions are on generated text.
///     That the line compiles against the real packages is measured by the Time off, Showcase and
///     Conformance hosts, which build with it.
/// </remarks>
public class ApiDocumentationHostWiringTests
{
    private const string HostStubs = """
        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }
        public static class Program
        {
            public static void Main() { }
        }
        """;

    private const string OpenApiStub = """

        namespace Pragmatic.Endpoints.OpenApi
        {
            public static class ApiDocumentationMapper { }
        }
        """;

    private const string ScalarStub = """

        namespace Scalar.AspNetCore
        {
            public static class ScalarEndpointRouteBuilderExtensions { }
        }
        """;

    private const string Mapper = "global::Pragmatic.Endpoints.OpenApi.ApiDocumentationMapper.Map(app";

    private static string? GetEntry(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Host.Entry"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    /// <summary>The control: without the package there is nothing to call.</summary>
    [Fact]
    public void AHostWithoutTheOpenApiPackage_PublishesNoDocument()
    {
        var entry = GetEntry(GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(HostStubs, []));

        entry.Should().NotBeNull("the host entry point is generated for every host");
        entry!.Should().NotContain("ApiDocumentationMapper");
    }

    [Fact]
    public void AHostWithTheOpenApiPackage_PublishesTheDocument_WithoutAReference()
    {
        var entry = GetEntry(GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            HostStubs + OpenApiStub, []));

        entry.Should().NotBeNull();
        entry!.Should().Contain(Mapper + ");")
            .And.NotContain("Scalar", "the host does not reference it, and generated code names only what it references");
    }

    [Fact]
    public void AHostWithScalarToo_PublishesTheDocumentWithScalarOverIt()
    {
        var entry = GetEntry(GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            HostStubs + OpenApiStub + ScalarStub, []));

        entry.Should().NotBeNull();
        entry!.Should().Contain(Mapper + ",")
            .And.Contain("new global::Pragmatic.Endpoints.OpenApi.InteractiveApiReference(\"/scalar\",")
            .And.Contain("global::Scalar.AspNetCore.ScalarEndpointRouteBuilderExtensions.MapScalarApiReference(endpoints)");
    }

    /// <summary>After every endpoint is mapped: a hand-written mapping must already be in the table.</summary>
    [Fact]
    public void TheDocumentIsPublishedAfterTheEndpointsAreMapped()
    {
        var entry = GetEntry(GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            HostStubs + OpenApiStub, []))!;

        entry.IndexOf(Mapper, StringComparison.Ordinal).Should().BeGreaterThan(
            entry.IndexOf("configurator.Configure(app);", StringComparison.Ordinal));
    }

    [Fact]
    public void ALibraryWithTheOpenApiPackage_GetsNoEntryPoint()
        => GetEntry(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(HostStubs + OpenApiStub, []))
            .Should().BeNull("library-mode compilations must not get host wiring");
}
