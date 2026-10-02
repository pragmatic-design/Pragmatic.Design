using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.Testing.SourceGenerator.Models;

namespace Pragmatic.Testing.SourceGenerator.Templates;

/// <summary>
///     Emits the typed test client for a boundary: <c>Api.{Boundary}.{Name}Async(client, ...)</c>
///     with routes/verbs resolved at compile time (from the app's ApiRoutes) and typed responses
///     (<c>ApiResponse&lt;T&gt;</c>). Usable by generated contract tests AND hand-written tests.
/// </summary>
internal sealed class ApiClientTemplate : CSharpTemplate
{
    private const string HttpClientType = "global::System.Net.Http.HttpClient";
    private const string RequestType = "global::System.Net.Http.HttpRequestMessage";
    private const string VerbType = "global::System.Net.Http.HttpMethod";
    private const string CancellationTokenType = "global::System.Threading.CancellationToken";
    private const string TaskType = "global::System.Threading.Tasks.Task";
    private const string JsonContentType = "global::System.Net.Http.Json.JsonContent";
    private const string PragmaticJsonType = "global::Pragmatic.Testing.PragmaticJson";
    private const string ApiResponseType = "global::Pragmatic.Testing.ApiResponse";

    private readonly string _boundary;
    private readonly IReadOnlyList<ApiClientOperationModel> _operations;

    public ApiClientTemplate(string boundary, IReadOnlyList<ApiClientOperationModel> operations)
    {
        _boundary = boundary;
        _operations = operations;
    }

    protected override string? GeneratorName => "Pragmatic.Testing.SourceGenerator";

    public override Artifact RenderOutput() => new($"_Api.{_boundary}.g.cs", ToSourceText());

    protected override bool Validate() => _operations.Count > 0;

    public override void RenderFile()
    {
        AppendLine("namespace Pragmatic.Tests.Generated;");
        AppendLine();
        AppendLine("/// <summary>Typed API client for tests — routes and verbs resolved at compile time.</summary>");
        AppendLine("public static partial class Api");
        AppendLine("{");
        IncreaseIndent();

        AppendLine($"/// <summary>Typed operations for the {_boundary} boundary.</summary>");
        AppendLine($"public static class {_boundary}");
        AppendLine("{");
        IncreaseIndent();

        var first = true;
        foreach (var operation in _operations.OrderBy(o => o.Name, System.StringComparer.Ordinal))
        {
            if (!first) AppendLine();
            first = false;
            RenderOperation(operation);
        }

        DecreaseIndent();
        AppendLine("}");
        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderOperation(ApiClientOperationModel operation)
    {
        var responseType = operation.ResponseType is { } response
            ? $"{ApiResponseType}<{response}>"
            : ApiResponseType;

        // Signature: client, [body], required builder params, ct, optional builder params
        var parameters = new List<string> { $"{HttpClientType} client" };
        if (operation.HasBody)
            parameters.Add("object body");
        foreach (var parameter in operation.Parameters.AsImmutableArray().Where(p => !p.IsOptional))
            parameters.Add($"{parameter.TypeName} {parameter.Name}");
        parameters.Add($"{CancellationTokenType} ct = default");
        foreach (var parameter in operation.Parameters.AsImmutableArray().Where(p => p.IsOptional))
            parameters.Add($"{parameter.TypeName} {parameter.Name} = null");

        var builderArgs = string.Join(", ", operation.Parameters.AsImmutableArray().Select(p => p.Name));

        AppendLine($"/// <summary>{operation.HttpMethod} via the compile-time route (see ApiRoutes).</summary>");
        AppendLine($"public static async {TaskType}<{responseType}> {operation.Name}Async(");
        AppendLine($"    {string.Join(", ", parameters)})");
        AppendLine("{");
        IncreaseIndent();

        AppendLine($"var __url = {operation.RouteBuilder}({builderArgs});");
        AppendLine($"using var __request = new {RequestType}({VerbType}.Parse(\"{operation.HttpMethod}\"), __url);");
        if (operation.HasBody)
            AppendLine($"__request.Content = {JsonContentType}.Create(body, options: {PragmaticJsonType}.Options);");
        AppendLine("var __response = await client.SendAsync(__request, ct).ConfigureAwait(false);");
        AppendLine($"return new {responseType}(__response);");

        DecreaseIndent();
        AppendLine("}");
    }
}
