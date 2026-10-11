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

    /// <summary>
    ///     A property whose name starts with an acronym is read and sent under the name the server writes.
    /// </summary>
    /// <remarks>
    ///     The host's policy is <c>JsonNamingPolicy.CamelCase</c>: <c>URL</c> is <c>url</c>, <c>IOStream</c>
    ///     is <c>ioStream</c>. A DTO interface that says <c>uRL</c> reads <c>undefined</c> from every
    ///     response, and a request interface that says <c>uRL</c> is what <c>JSON.stringify</c> sends (#146).
    /// </remarks>
    [Fact]
    public void Generate_AnAcronymLedProperty_IsNamedAsTheServerWritesIt()
    {
        var manifest = new ClientManifest
        {
            Assembly = "Acme.Catalog",
            Types =
            [
                new ClientType
                {
                    SimpleName = "LinkDto",
                    Kind = "dto",
                    Properties =
                    [
                        new ClientProperty { Name = "URL", Type = "System.String" },
                        new ClientProperty { Name = "IOStream", Type = "System.String" },
                    ],
                }
            ],
            Endpoints =
            [
                new ClientEndpoint
                {
                    OperationId = "Catalog.CreateLink",
                    HttpMethod = "POST",
                    FullRoute = "/links",
                    IsVoid = true,
                    RequestBody = new ClientBody
                    {
                        Properties = [new ClientProperty { Name = "URL", Type = "System.String", IsRequired = true }],
                    },
                }
            ],
        };

        new TypeScriptClientGenerator(manifest, "@acme/catalog", _outDir).Generate();

        var types = Read("src", "types.ts");
        types.Should().Contain("export interface LinkDto {\n  url: string;\n  ioStream: string;\n}".ReplaceLineEndings());
        types.Should().Contain("export interface CreateLinkRequest {\n  url: string;\n}".ReplaceLineEndings());
    }

    /// <summary>
    ///     A property renamed with <c>[JsonPropertyName]</c> is read and sent under its wire name, which the
    ///     manifest carries as <c>wireName</c> (#180).
    /// </summary>
    [Fact]
    public void Generate_ARenamedProperty_IsNamedByItsWireName()
    {
        // Read from JSON as the CLI reads the manifest, so the test exercises the manifest's own key.
        var manifest = System.Text.Json.JsonSerializer.Deserialize<ClientManifest>("""
            {
              "assembly": "Acme.Catalog",
              "types": [{ "simpleName": "ThingDto", "kind": "dto",
                          "properties": [{ "name": "Name", "wireName": "label", "type": "System.String" },
                                         { "name": "Weight", "type": "System.Int32" }] }],
              "endpoints": [{ "operationId": "Catalog.CreateThing", "httpMethod": "POST", "fullRoute": "/things",
                              "isVoid": true,
                              "requestBody": { "properties": [{ "name": "Name", "wireName": "label", "type": "System.String", "isRequired": true }] } }]
            }
            """, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        new TypeScriptClientGenerator(manifest, "@acme/catalog", _outDir).Generate();

        var types = Read("src", "types.ts");
        types.Should().Contain("export interface ThingDto {\n  label: string;\n  weight: number;\n}".ReplaceLineEndings());
        types.Should().Contain("export interface CreateThingRequest {\n  label: string;\n}".ReplaceLineEndings());
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
