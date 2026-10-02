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

    [Fact]
    public void Emit_Paging_DefinesPagedResult()
    {
        var (module, json) = LoadSampleModule();

        TsEmitter.Emit(module, json)["paging.ts"]
            .Should().Contain("export interface PagedResult<T> {");
    }
}
