using Pragmatic.Testing.Assertions;
using Pragmatic.Client.Cli;
using Xunit;

namespace Pragmatic.Client.Tests.Cli;

/// <summary>
///     pragmatic-client TS emitter over the sample manifest: typed operations, DTO interfaces,
///     error-code union, PagedResult, and the manifest hash header on every file.
/// </summary>
public class TsEmitterTests
{
    private static (ManifestJson.ManifestDoc Module, string Json) LoadSampleModule()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PragmaticManifest.json"));
        var modules = ManifestJson.Read(json);
        return (modules[0], json);
    }

    [Fact]
    public void Emit_SampleManifest_ProducesAllFiles()
    {
        var (module, json) = LoadSampleModule();

        var files = TsEmitter.Emit(module, json);

        files.Keys.Should().BeEquivalentTo("types.ts", "errors.ts", "client.ts", "paging.ts");
        files.Values.Should().OnlyContain(content => content.StartsWith("// pragmatic-manifest/v1 sha256:"),
            "every file must carry the manifest hash for drift detection");
    }

    [Fact]
    public void Emit_Client_HasTypedOperationsAndResult()
    {
        var (module, json) = LoadSampleModule();

        var client = TsEmitter.Emit(module, json)["client.ts"];

        client.Should().Contain($"export class {module.BoundaryName}Client");
        client.Should().Contain("export type ApiResult<TValue> =");
        client.Should().Contain("{ ok: true; value: TValue; status: number }");
        client.Should().Contain("encodeURIComponent(");
        client.Should().Contain("private async send<TValue>(");
    }

    [Fact]
    public void Emit_Types_MapClrTypesToTs()
    {
        var (module, json) = LoadSampleModule();

        var types = TsEmitter.Emit(module, json)["types.ts"];

        types.Should().Contain("export interface ");
        types.Should().NotContain("System.", "CLR type names must be mapped to TS primitives");
    }

    [Fact]
    public void Emit_Errors_UnionOfCodes()
    {
        var (module, json) = LoadSampleModule();

        var errors = TsEmitter.Emit(module, json)["errors.ts"];

        errors.Should().Contain("export type ApiErrorCode =");
        errors.Should().Contain("export interface ApiError {");
    }

    /// <summary>
    ///     A property whose name starts with an acronym is read and sent under the name the server writes.
    /// </summary>
    /// <remarks>
    ///     The host's policy is <c>JsonNamingPolicy.CamelCase</c>: <c>URL</c> is <c>url</c>, <c>IOStream</c>
    ///     is <c>ioStream</c>. An interface that says <c>uRL</c> reads <c>undefined</c> from every response,
    ///     and a body that says <c>uRL</c> sends a field the server binds to nothing case-sensitively (#146).
    /// </remarks>
    [Fact]
    public void Emit_AnAcronymLedProperty_IsNamedAsTheServerWritesIt()
    {
        const string json = """
            {
              "assembly": "Acme.Catalog",
              "endpoints": [{ "operationId": "Catalog.CreateLink", "httpMethod": "POST", "fullRoute": "/links",
                              "isVoid": false, "response": { "type": "Acme.Catalog.LinkDto" },
                              "requestBody": { "properties": [{ "name": "URL", "type": "string", "isRequired": true }] } }],
              "types": [{ "simpleName": "LinkDto", "kind": "dto",
                          "properties": [{ "name": "URL", "type": "string", "isRequired": true },
                                         { "name": "IOStream", "type": "string", "isRequired": true }] }]
            }
            """;

        var files = TsEmitter.Emit(ManifestJson.Read(json)[0], json);

        files["types.ts"].Should().Contain("  url: string;");
        files["types.ts"].Should().Contain("  ioStream: string;");
        files["types.ts"].Should().NotContain("uRL");
        files["client.ts"].Should().Contain("body: { url: string }");
    }

    /// <summary>
    ///     An endpoint with no response type is in the client, returning <c>ApiResult&lt;void&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     The manifest writes <c>response</c> only when the endpoint has a response type, and the filter
    ///     was a nested property pattern (<c>Response.IsStream: not true</c>), which never matches a null
    ///     <c>Response</c>: every such endpoint was left out of the client (#179).
    /// </remarks>
    [Fact]
    public void Emit_AnEndpointWithNoResponseType_IsAnOperationReturningVoid()
    {
        const string json = """
            {
              "assembly": "Acme.Catalog",
              "endpoints": [{ "operationId": "Catalog.DeleteLink", "httpMethod": "DELETE", "fullRoute": "/links/{id}",
                              "isVoid": true,
                              "parameters": [{ "name": "id", "in": "path", "type": "System.Guid", "isRequired": true }] }]
            }
            """;

        var client = TsEmitter.Emit(ManifestJson.Read(json)[0], json)["client.ts"];

        client.Should().Contain("deleteLink(id: string): Promise<ApiResult<void>> {");
    }

    /// <summary>Control: a streaming endpoint is still not a request-response operation of the client.</summary>
    [Fact]
    public void Emit_AStreamingEndpoint_IsLeftOut()
    {
        const string json = """
            {
              "assembly": "Acme.Catalog",
              "endpoints": [{ "operationId": "Catalog.WatchLinks", "httpMethod": "GET", "fullRoute": "/links/watch",
                              "isVoid": false, "response": { "type": "System.String", "isStream": true } }]
            }
            """;

        var client = TsEmitter.Emit(ManifestJson.Read(json)[0], json)["client.ts"];

        client.Should().NotContain("watchLinks(");
    }

    /// <summary>
    ///     A property renamed with <c>[JsonPropertyName]</c> is read and sent under its wire name, which the
    ///     manifest carries as <c>wireName</c> (#180).
    /// </summary>
    [Fact]
    public void Emit_ARenamedProperty_IsNamedByItsWireName()
    {
        const string json = """
            {
              "assembly": "Acme.Catalog",
              "endpoints": [{ "operationId": "Catalog.CreateThing", "httpMethod": "POST", "fullRoute": "/things",
                              "isVoid": true,
                              "requestBody": { "properties": [{ "name": "Name", "wireName": "label", "type": "string", "isRequired": true }] } }],
              "types": [{ "simpleName": "ThingDto", "kind": "dto",
                          "properties": [{ "name": "Name", "wireName": "label", "type": "string", "isRequired": true },
                                         { "name": "Weight", "type": "int", "isRequired": true }] }]
            }
            """;

        var files = TsEmitter.Emit(ManifestJson.Read(json)[0], json);

        files["types.ts"].Should().Contain("  label: string;");
        files["types.ts"].Should().Contain("  weight: number;");
        files["types.ts"].Should().NotContain("  name: string;");
        files["client.ts"].Should().Contain("body: { label: string }");
    }

    [Fact]
    public void Emit_Paging_DefinesPagedResult()
    {
        var (module, json) = LoadSampleModule();

        TsEmitter.Emit(module, json)["paging.ts"]
            .Should().Contain("export interface PagedResult<T> {");
    }
}
