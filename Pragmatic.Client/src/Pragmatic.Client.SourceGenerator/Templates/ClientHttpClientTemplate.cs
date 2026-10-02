using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;

namespace Pragmatic.Client.SourceGenerator.Templates;

/// <summary>
///     Emits <c>{Boundary}HttpClient</c>: the implementation behind the generated interface. Builds each
///     request's URL (path parameters URL-encoded, query values appended and culture-invariantly formatted),
///     issues it with the verb the manifest declares, and maps a non-success response onto a typed
///     <c>IError</c> instead of throwing.
/// </summary>
internal sealed class ClientHttpClientTemplate(
    string clientNamespace,
    string boundary,
    List<PragmaticClientGenerator.EndpointDto> endpoints,
    List<PragmaticClientGenerator.ErrorInfo> errorTypes) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Client.SourceGenerator";

    protected override string? SourceInfo => $"API manifest for {boundary}";

    public override Artifact RenderOutput() => new($"{boundary}HttpClient.g.cs", ToSourceText());

    protected override bool Validate() => endpoints.Count > 0;

    public override void RenderFile()
    {
        AddUsing("System.Collections.Generic");
        AddUsing("System.Net.Http.Json");
        AddUsing("System.Text.Json");
        AddUsing("Pragmatic");
        AddUsing("Pragmatic.Client");
        AppendNamespace(clientNamespace);
        AppendLine();
        AppendLine($"internal sealed class {boundary}HttpClient : I{boundary}Client");
        AppendLine("{");
        AppendLine("    private readonly HttpClient _http;");
        AppendLine($"    public {boundary}HttpClient(HttpClient http) => _http = http;");
        AppendLine();

        foreach (var ep in endpoints)
        {
            var method = ep.OperationId?.Split('.').LastOrDefault() ?? "Unknown";
            var returnType = ep.IsVoid
                ? "Pragmatic.Result.VoidResult<Pragmatic.Result.IError>"
                : $"Pragmatic.Result.Result<{PragmaticClientGenerator.SimplifyType(ep.Response?.Type)}, Pragmatic.Result.IError>";
            var httpMethod = (ep.HttpMethod ?? "GET").ToUpperInvariant();
            var route = ep.FullRoute ?? "/";
            var @params = PragmaticClientGenerator.BuildParams(ep);

            AppendLine($"    public async Task<{returnType}> {method}({@params}CancellationToken ct = default)");
            AppendLine("    {");

            AppendUrlBuilding(ep, route);

            var hasBody = PragmaticClientGenerator.HasJsonBody(ep);
            switch (httpMethod)
            {
                case "GET":
                    AppendLine("        var response = await _http.GetAsync(url, ct).ConfigureAwait(false);");
                    break;
                case "DELETE":
                    AppendLine("        var response = await _http.DeleteAsync(url, ct).ConfigureAwait(false);");
                    break;
                default:
                    if (hasBody)
                        AppendLine($"        var response = await _http.{PragmaticClientGenerator.MethodForVerb(httpMethod)}(url, request, Info<{RequestTypeOf(ep)}>(), ct).ConfigureAwait(false);");
                    else
                        AppendLine($"        var response = await _http.SendAsync(new HttpRequestMessage(new HttpMethod(\"{httpMethod}\"), url), ct).ConfigureAwait(false);");
                    break;
            }

            if (ep.IsVoid)
            {
                AppendLine("        if (response.IsSuccessStatusCode) return Pragmatic.Result.VoidResult<Pragmatic.Result.IError>.Success();");
                AppendLine("        return Pragmatic.Result.VoidResult<Pragmatic.Result.IError>.Failure(await MapErrorAsync(response, ct).ConfigureAwait(false));");
            }
            else
            {
                var respType = PragmaticClientGenerator.SimplifyType(ep.Response?.Type);
                AppendLine($"        if (response.IsSuccessStatusCode)");
                AppendLine("        {");
                // Typed: the untyped overload is RequiresUnreferencedCode, so a trimmed or AOT-published
                // consumer — which is the whole point of a generated SDK — gets warnings or an empty payload.
                AppendLine($"            var payload = await response.Content.ReadFromJsonAsync(Info<{respType}>(), ct).ConfigureAwait(false);");

                if (PragmaticClientGenerator.IsReferenceResponseType(respType))
                {
                    // A 2xx with no body (a 204, or a proxy stripping it) deserializes to null. Converting that
                    // implicitly to Result<T, IError> would hand the caller a success carrying nothing.
                    AppendLine("            if (payload is null)");
                    AppendLine($"                return Pragmatic.Result.Result<{respType}, Pragmatic.Result.IError>.Failure(");
                    AppendLine("                    new ApiError { Code = \"EMPTY_RESPONSE\", Title = \"The server returned a success status with no body.\", StatusCode = (int)response.StatusCode });");
                }

                AppendLine("            return payload;");
                AppendLine("        }");
                AppendLine();
                AppendLine($"        return Pragmatic.Result.Result<{respType}, Pragmatic.Result.IError>.Failure(await MapErrorAsync(response, ct).ConfigureAwait(false));");
            }

            AppendLine("    }");
            AppendLine();
        }

        // Only emitted when some endpoint actually carries query values, so the class never holds dead code.
        if (endpoints.Any(e => PragmaticClientGenerator.CollectQueryValues(e).Count > 0))
        {
            GenerateFormatQueryValue();
            AppendLine();
        }

        RenderTypeInfoHelper();
        AppendLine();

        // Generate MapErrorAsync — reads ProblemDetails, switches on code
        GenerateMapErrorMethod(errorTypes);

        AppendLine("}");
    }

    /// <summary>
    ///     Emits the <c>url</c> local: the route with each path parameter URL-encoded, followed by the query
    ///     string built from the optional query parameters that the caller actually supplied.
    /// </summary>
    private void AppendUrlBuilding(PragmaticClientGenerator.EndpointDto ep, string route)
    {
        var pathParams = PragmaticClientGenerator.CollectPathParams(ep);
        var queryValues = PragmaticClientGenerator.CollectQueryValues(ep);

        if (pathParams.Count > 0)
        {
            var interpolated = route;
            foreach (var p in pathParams)
                interpolated = PragmaticClientGenerator.ReplaceRouteToken(interpolated, p.Name, $"{{System.Uri.EscapeDataString({p.Name}.ToString()!)}}");
            AppendLine($"        var url = $\"{interpolated}\";");
        }
        else
        {
            AppendLine($"        var url = \"{route}\";");
        }

        if (queryValues.Count == 0)
            return;

        AppendLine("        var query = new List<string>();");
        foreach (var q in queryValues)
        {
            var local = PragmaticClientGenerator.CamelCase(q.Name);
            AppendLine($"        if ({local} is not null)");
            AppendLine($"            query.Add(\"{Uri.EscapeDataString(q.Name)}=\" + System.Uri.EscapeDataString(FormatQueryValue({local})));");
        }

        AppendLine("        if (query.Count > 0)");
        AppendLine("            url += \"?\" + string.Join(\"&\", query);");
    }

    /// <summary>
    ///     Emits the helper that renders a query value culture-invariantly. Without it, a
    ///     <c>DateTimeOffset</c> or a <c>decimal</c> would be formatted with the caller's culture and the
    ///     server would either reject it or bind a different value.
    /// </summary>
    private void GenerateFormatQueryValue()
    {
        AppendLine("    private static string FormatQueryValue(object value) => value switch");
        AppendLine("    {");
        AppendLine("        System.DateTimeOffset dto => dto.ToString(\"O\", System.Globalization.CultureInfo.InvariantCulture),");
        AppendLine("        System.DateTime dt => dt.ToString(\"O\", System.Globalization.CultureInfo.InvariantCulture),");
        AppendLine("        System.DateOnly d => d.ToString(\"O\", System.Globalization.CultureInfo.InvariantCulture),");
        AppendLine("        System.TimeOnly t => t.ToString(\"O\", System.Globalization.CultureInfo.InvariantCulture),");
        AppendLine("        bool b => b ? \"true\" : \"false\",");
        AppendLine("        System.IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),");
        AppendLine("        _ => value.ToString() ?? string.Empty");
        AppendLine("    };");
    }

    private void GenerateMapErrorMethod(List<PragmaticClientGenerator.ErrorInfo> errorTypes)
    {
        AppendLine("    private static async Task<Pragmatic.Result.IError> MapErrorAsync(HttpResponseMessage response, CancellationToken ct)");
        AppendLine("    {");
        AppendLine("        var statusCode = (int)response.StatusCode;");
        AppendLine("        string? code = null;");
        AppendLine("        string? title = null;");
        AppendLine("        string? detail = null;");
        AppendLine("        JsonElement root = default;");
        AppendLine("        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);");
        AppendLine("        try");
        AppendLine("        {");
        AppendLine("            if (!string.IsNullOrEmpty(body))");
        AppendLine("            {");
        AppendLine("                using var doc = JsonDocument.Parse(body);");
        AppendLine("                root = doc.RootElement.Clone();");
        AppendLine("                if (root.ValueKind == JsonValueKind.Object)");
        AppendLine("                {");
        AppendLine("                    if (root.TryGetProperty(\"code\", out var codeProp))");
        AppendLine("                        code = codeProp.GetString();");
        AppendLine("                    if (root.TryGetProperty(\"title\", out var titleProp))");
        AppendLine("                        title = titleProp.GetString();");
        AppendLine("                    if (root.TryGetProperty(\"detail\", out var detailProp))");
        AppendLine("                        detail = detailProp.GetString();");
        AppendLine("                }");
        AppendLine("            }");
        AppendLine("        }");
        AppendLine("        catch (JsonException ex)");
        AppendLine("        {");
        // Swallowing this silently loses the only clue about what the server actually returned (an HTML
        // error page, a proxy response). Surfacing it as Detail keeps the failure diagnosable.
        AppendLine("            detail = \"The error response was not valid JSON (\" + ex.Message + \"). Body: \" +");
        AppendLine("                (body.Length > 512 ? body.Substring(0, 512) + \"…\" : body);");
        AppendLine("        }");
        AppendLine();

        if (errorTypes.Count > 0)
        {
            AppendLine("        return code switch");
            AppendLine("        {");
            foreach (var error in errorTypes)
            {
                if (error.Extensions.Count > 0)
                {
                    // Typed error with extension properties populated from ProblemDetails
                    Append($"            \"{PragmaticClientGenerator.EscapeLiteral(error.Code)}\" => new {error.TypeName} {{ ");
                    var inits = new List<string>();
                    foreach (var ext in error.Extensions)
                    {
                        if (ext.Name is null) continue;
                        var propName = char.ToUpperInvariant(ext.Name[0]) + ext.Name.Substring(1);
                        var reader = PragmaticClientGenerator.GetJsonReader(ext.Name, ext.Type);
                        inits.Add($"{propName} = {reader}");
                    }
                    Append(string.Join(", ", inits));
                    AppendLine(" },");
                }
                else
                {
                    AppendLine($"            \"{PragmaticClientGenerator.EscapeLiteral(error.Code)}\" => new {error.TypeName}(),");
                }
            }
            AppendLine("            _ => new ApiError { Code = code ?? \"UNKNOWN\", Title = title ?? \"Request failed\", StatusCode = statusCode, Detail = detail }");
            AppendLine("        };");
        }
        else
        {
            AppendLine("        return new ApiError { Code = code ?? \"UNKNOWN\", Title = title ?? \"Request failed\", StatusCode = statusCode, Detail = detail };");
        }

        AppendLine("    }");
    }


    /// <summary>The request DTO an endpoint sends, by the same rule that generated it.</summary>
    private static string RequestTypeOf(PragmaticClientGenerator.EndpointDto ep)
        => (ep.OperationId?.Split('.').LastOrDefault() ?? "Unknown") + "Request";

    /// <summary>
    ///     Emits the typed-metadata helper every call goes through.
    /// </summary>
    /// <remarks>
    ///     Resolved from the generated context, which this generator writes longhand — a
    ///     <c>[JsonSerializable]</c> context would need System.Text.Json's own generator, and one source
    ///     generator does not see another's output.
    /// </remarks>
    private void RenderTypeInfoHelper()
    {
        AppendLine("    /// <summary>Metadata for a type this client puts on the wire.</summary>");
        AppendLine("    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> Info<T>()");
        AppendLine($"        => {clientNamespace}.Generated.PragmaticJsonContext.Default.GetTypeInfo(typeof(T))");
        AppendLine("               as System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>");
        AppendLine("           ?? throw new System.InvalidOperationException(");
        AppendLine("               $\"The generated client context does not cover '{typeof(T)}'. This is a generator bug: \" +");
        AppendLine("               \"every type an endpoint sends or reads should be in it.\");");
    }
}
