// Pragmatic.Composition.HostWiring.Tests - A host whose modules publish no operation yet

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     The skeleton compiles: a host whose module has no operation yet still gets the manifest
///     its generated registration names.
/// </summary>
/// <remarks>
///     <para>
///         <c>pragmatic-new-app</c> tells a user to build this first — a module with nothing in it, and a
///         host that includes it. The registration is decided by which types the host references; were
///         the class decided by whether any module has published a manifest, the skeleton would fail
///         with CS0103 on <c>PragmaticManifest</c>. The OpenAPI document beside it has the same split and
///         gets an empty document; the manifest gets the same answer.
///     </para>
///     <para>
///         This host references <c>Pragmatic.Endpoints.OpenApi</c>, where <c>HostManifest</c> lives,
///         because the registration is only emitted when it does; no other host in this suite does.
///     </para>
/// </remarks>
public sealed class AHostWithNothingPublishedYetCompilesTests
{
    private const string EmptyModule = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;

        namespace Catalog
        {
            [Module(Name = "Catalog")]
            public sealed class CatalogModule;

            [Boundary]
            public partial class CatalogBoundary;
        }
        """;

    private const string EndpointModule = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace Catalog
        {
            [Module(Name = "Catalog")]
            public sealed class CatalogModule;

            [Boundary]
            public partial class CatalogBoundary;

            [Endpoint(HttpVerb.Get, "api/ping")]
            public partial class PingEndpoint : Endpoint<string>
            {
                public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<string>.Success("pong"));
            }
        }
        """;

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;

        namespace Shop.Host;

        [Module]
        [Include<Catalog.CatalogModule>]
        public sealed class ShopHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void AModuleWithNoOperation_TheHostCompiles_WithAnEmptyManifest()
    {
        var (errors, generated) = ModuleAndHost.Generate("Catalog", [EmptyModule], "Shop.Host", Host);

        errors.Should().BeEmpty("a skeleton is the first state that has to build");

        generated.Should().ContainKey("_Metadata.PragmaticManifest.Aggregated.g.cs",
            "the host registers the manifest, so the manifest has to exist");
        var manifest = generated["_Metadata.PragmaticManifest.Aggregated.g.cs"];
        manifest.Should().Contain("\"moduleCount\": 0");
        manifest.Should().Contain("ModuleCount = 0");
    }

    /// <summary>The control: a module that publishes an endpoint is in the manifest.</summary>
    [Fact]
    public void AModuleWithAnEndpoint_IsInTheManifest()
    {
        var (errors, generated) = ModuleAndHost.Generate("Catalog", [EndpointModule], "Shop.Host", Host);

        errors.Should().BeEmpty();
        generated["_Metadata.PragmaticManifest.Aggregated.g.cs"].Should().Contain("\"moduleCount\": 1");
    }
}
