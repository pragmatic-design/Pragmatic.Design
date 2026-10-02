using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Cli.ClientGen;

namespace Pragmatic.Migrations.Core.Tests.Unit.Cli;

/// <summary>
///     Unit tests for <see cref="TypeScriptClientGenerator"/>. Writes to an isolated temp directory
///     and asserts on the emitted TypeScript text — including the query-parameter-on-URL fix.
/// </summary>
public class TypeScriptClientGeneratorTests : IDisposable
{
    private readonly string _outDir =
        Path.Combine(Path.GetTempPath(), "pragmatic-tsgen-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_outDir))
            Directory.Delete(_outDir, recursive: true);
    }

    private string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { _outDir }.Concat(parts).ToArray()));

    [Fact]
    public void Generate_GetEndpointWithQueryParams_AppendsThemToUrl()
    {
        var manifest = new ClientManifest
        {
            Assembly = "Acme.Catalog",
            Endpoints =
            [
                new ClientEndpoint
                {
                    OperationId = "Catalog.Search",
                    HttpMethod = "GET",
                    FullRoute = "/products",
                    IsVoid = false,
                    Response = new ClientResponse { Type = "System.String" },
                    Parameters =
                    [
                        new ClientParam { Name = "term", In = "query", Type = "System.String" },
                        new ClientParam { Name = "page", In = "query", Type = "System.Int32" },
                    ],
                }
            ],
        };

        new TypeScriptClientGenerator(manifest, "@acme/catalog", _outDir).Generate();

        var client = Read("src", "client.ts");
        // Query params are serialized onto the URL, not dropped.
        client.Should().Contain("new URL(");
        client.Should().Contain("url.searchParams.set('term', String(term))");
        client.Should().Contain("url.searchParams.set('page', String(page))");
    }

    [Fact]
    public void Generate_EnumType_ProducesUnionType()
    {
        var manifest = new ClientManifest
        {
            Assembly = "Acme.Catalog",
            Types =
            [
                new ClientType
                {
                    SimpleName = "Status",
                    Kind = "enum",
                    EnumValues = ["Active", "Archived"],
                }
            ],
        };

        new TypeScriptClientGenerator(manifest, "@acme/catalog", _outDir).Generate();

        var types = Read("src", "types.ts");
        types.Should().Contain("export type Status = 'Active' | 'Archived';");
    }

    [Fact]
    public void Generate_EntityType_ProducesCamelCasedDtoInterface()
    {
        var manifest = new ClientManifest
        {
            Assembly = "Acme.Catalog",
            Types =
            [
                new ClientType
                {
                    SimpleName = "Product",
                    Kind = "entity",
                    Properties =
                    [
                        new ClientProperty { Name = "ProductId", Type = "System.Guid" },
                        new ClientProperty { Name = "DisplayName", Type = "System.String", IsNullable = true },
                    ],
                }
            ],
        };

        new TypeScriptClientGenerator(manifest, "@acme/catalog", _outDir).Generate();

        var types = Read("src", "types.ts");
        types.Should().Contain("export interface ProductDto {");
        types.Should().Contain("productId: string;");
        types.Should().Contain("displayName?: string | null;");
    }

    [Fact]
    public void Generate_AlwaysEmitsResultTypesAndBarrelExport()
    {
        var manifest = new ClientManifest { Assembly = "Acme.Catalog", Endpoints = [] };

        new TypeScriptClientGenerator(manifest, "@acme/catalog", _outDir).Generate();

        var types = Read("src", "types.ts");
        types.Should().Contain("export type Result<T, E>");
        types.Should().Contain("export interface ApiError");

        var index = Read("src", "index.ts");
        index.Should().Contain("export * from './types';");
        index.Should().Contain("export * from './client';");
    }
}
