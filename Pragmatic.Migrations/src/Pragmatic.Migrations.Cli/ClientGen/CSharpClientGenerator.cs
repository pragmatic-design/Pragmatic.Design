using System.Text;

namespace Pragmatic.Migrations.Cli.ClientGen;

/// <summary>
///     Generates a C# client project from manifest data.
///     Produces: interfaces, HTTP client, models, errors, DI registration.
/// </summary>
public sealed class CSharpClientGenerator(ClientManifest manifest, string ns, string outputDir)
{
    public void Generate()
    {
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(Path.Combine(outputDir, "Models"));
        Directory.CreateDirectory(Path.Combine(outputDir, "Errors"));

        GenerateCsproj();
        GenerateInterface();
        GenerateHttpClient();
        GenerateModels();
        GenerateErrors();
        GenerateRegistration();
    }

    private void GenerateCsproj()
    {
        var content = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Pragmatic.Result" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(outputDir, $"{ns}.csproj"), content);
    }

    private void GenerateInterface()
    {
        if (manifest.Endpoints is not { Count: > 0 }) return;

        var sb = new StringBuilder();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Domain-style typed client interface.</summary>");
        sb.AppendLine($"public interface I{GetBoundaryName()}Client");
        sb.AppendLine("{");

        foreach (var ep in manifest.Endpoints)
        {
            var methodName = ep.OperationId?.Split('.').Last() ?? "Unknown";
            var returnType = ep.IsVoid ? "VoidResult<IError>" : $"Result<{SimplifyType(ep.Response?.Type ?? "object")}, IError>";
            var parameters = BuildParameterList(ep);

            if (ep.Summary is not null)
                sb.AppendLine($"    /// <summary>{ep.Summary}</summary>");
            sb.AppendLine($"    Task<{returnType}> {methodName}({parameters}CancellationToken ct = default);");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(outputDir, $"I{GetBoundaryName()}Client.cs"), sb.ToString());
    }

    private void GenerateHttpClient()
    {
        if (manifest.Endpoints is not { Count: > 0 }) return;

        var sb = new StringBuilder();
        sb.AppendLine("using System.Net.Http.Json;");
        sb.AppendLine("using Pragmatic;");
        sb.AppendLine($"using {ns}.Errors;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"internal sealed class {GetBoundaryName()}HttpClient(HttpClient http) : I{GetBoundaryName()}Client");
        sb.AppendLine("{");

        foreach (var ep in manifest.Endpoints)
        {
            var methodName = ep.OperationId?.Split('.').Last() ?? "Unknown";
            var returnType = ep.IsVoid ? "VoidResult<IError>" : $"Result<{SimplifyType(ep.Response?.Type ?? "object")}, IError>";
            var parameters = BuildParameterList(ep);
            var httpMethod = (ep.HttpMethod ?? "GET").ToUpperInvariant();
            var route = ep.FullRoute ?? "/";

            // Replace route parameters
            var routeExpr = $"\"{route}\"";
            if (ep.Parameters is { Count: > 0 })
            {
                foreach (var p in ep.Parameters.Where(p => p.In == "path"))
                    routeExpr = routeExpr.Replace($"{{{p.Name}}}", $"{{{p.Name}}}");
                routeExpr = $"$" + routeExpr;
            }

            sb.AppendLine($"    public async Task<{returnType}> {methodName}({parameters}CancellationToken ct = default)");
            sb.AppendLine("    {");

            switch (httpMethod)
            {
                case "GET":
                    sb.AppendLine($"        var response = await http.GetAsync({routeExpr}, ct).ConfigureAwait(false);");
                    break;
                case "POST":
                    sb.AppendLine($"        var response = await http.PostAsJsonAsync({routeExpr}, request, ct).ConfigureAwait(false);");
                    break;
                case "PUT":
                    sb.AppendLine($"        var response = await http.PutAsJsonAsync({routeExpr}, request, ct).ConfigureAwait(false);");
                    break;
                case "DELETE":
                    sb.AppendLine($"        var response = await http.DeleteAsync({routeExpr}, ct).ConfigureAwait(false);");
                    break;
            }

            if (ep.IsVoid)
            {
                sb.AppendLine("        if (response.IsSuccessStatusCode) return VoidResult<IError>.Success();");
            }
            else
            {
                sb.AppendLine($"        if (response.IsSuccessStatusCode)");
                sb.AppendLine($"            return await response.Content.ReadFromJsonAsync<{SimplifyType(ep.Response?.Type ?? "object")}>(ct).ConfigureAwait(false);");
            }

            // Return a typed error result instead of throwing — follows Result-over-exceptions convention.
            if (ep.IsVoid)
                sb.AppendLine("        return VoidResult<IError>.Failure(await MapErrorAsync(response, ct).ConfigureAwait(false));");
            else
                sb.AppendLine($"        return Result<{SimplifyType(ep.Response?.Type ?? "object")}, IError>.Failure(await MapErrorAsync(response, ct).ConfigureAwait(false));");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // Shared error mapper: parse an RFC 7807 ProblemDetails body when present,
        // otherwise fall back to a bare status-code error. Never throws — a malformed or
        // empty error body degrades gracefully to the status-code-only error.
        sb.AppendLine("    private static async Task<HttpRequestError> MapErrorAsync(HttpResponseMessage response, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        var statusCode = (int)response.StatusCode;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsPayload>(ct).ConfigureAwait(false);");
        sb.AppendLine("            if (problem is not null)");
        sb.AppendLine("                return new HttpRequestError(statusCode, problem.Title, problem.Detail);");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (Exception ex) when (ex is System.Text.Json.JsonException or HttpRequestException or NotSupportedException)");
        sb.AppendLine("        {");
        sb.AppendLine("            // Non-ProblemDetails / empty error body — fall through to status-only error.");
        sb.AppendLine("        }");
        sb.AppendLine("        return new HttpRequestError(statusCode);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private sealed record ProblemDetailsPayload(string? Title, string? Detail, int? Status);");
        sb.AppendLine();

        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(outputDir, $"{GetBoundaryName()}HttpClient.cs"), sb.ToString());
    }

    private void GenerateModels()
    {
        if (manifest.Types is null) return;

        foreach (var type in manifest.Types.Where(t => t.Kind == "entity" && t.Properties is { Count: > 0 }))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"namespace {ns}.Models;");
            sb.AppendLine();
            sb.AppendLine($"public sealed record {type.SimpleName}Dto");
            sb.AppendLine("{");
            foreach (var p in type.Properties!)
            {
                var csType = MapToCSharpType(p.Type ?? "string", p.IsNullable);
                sb.AppendLine($"    public {csType} {p.Name} {{ get; init; }}");
            }
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(outputDir, "Models", $"{type.SimpleName}Dto.cs"), sb.ToString());
        }
    }

    private void GenerateErrors()
    {
        // Always emit a shared HttpRequestError for HTTP-level failures
        var httpErrorContent = string.Join(Environment.NewLine,
            "using Pragmatic;",
            "",
            $"namespace {ns}.Errors;",
            "",
            "/// <summary>Represents an HTTP-level error returned by the remote endpoint.</summary>",
            "/// <remarks>Maps RFC 7807 ProblemDetails fields (title/detail) when the response carries them.</remarks>",
            "public sealed record HttpRequestError(int StatusCode, string? ProblemTitle = null, string? Detail = null) : IError",
            "{",
            "    public string Code => $\"HTTP_{StatusCode}\";",
            "    public string Title => ProblemTitle ?? $\"HTTP error {StatusCode}\";",
            "}");
        File.WriteAllText(Path.Combine(outputDir, "Errors", "HttpRequestError.cs"), httpErrorContent);

        if (manifest.Types is null) return;

        foreach (var type in manifest.Types.Where(t => t.Kind == "error"))
        {
            var sb = new StringBuilder();
            sb.AppendLine("using Pragmatic;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}.Errors;");
            sb.AppendLine();
            sb.AppendLine($"public sealed record {type.SimpleName} : IError");
            sb.AppendLine("{");
            sb.AppendLine($"    public string Code => \"{type.ErrorCode}\";");
            sb.AppendLine($"    public int StatusCode => {type.ErrorStatusCode ?? 500};");
            sb.AppendLine($"    public string Title => \"{type.SimpleName}\";");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(outputDir, "Errors", $"{type.SimpleName}.cs"), sb.ToString());
        }
    }

    private void GenerateRegistration()
    {
        var boundary = GetBoundaryName();
        var content = $$"""
            using Microsoft.Extensions.DependencyInjection;

            namespace {{ns}};

            public static class {{boundary}}ClientExtensions
            {
                public static IServiceCollection Add{{boundary}}Client(
                    this IServiceCollection services, string baseUrl, Action<HttpClient>? configure = null)
                {
                    services.AddHttpClient<I{{boundary}}Client, {{boundary}}HttpClient>(client =>
                    {
                        client.BaseAddress = new Uri(baseUrl);
                        configure?.Invoke(client);
                    });
                    return services;
                }
            }
            """;
        File.WriteAllText(Path.Combine(outputDir, $"{boundary}ClientExtensions.cs"), content);
    }

    private string GetBoundaryName()
    {
        var assembly = manifest.Assembly ?? "App";
        var parts = assembly.Split('.');
        return parts.Length >= 2 ? parts[^1] : parts[0];
    }

    private static string BuildParameterList(ClientEndpoint ep)
    {
        var parts = new List<string>();

        if (ep.Parameters is { Count: > 0 })
            foreach (var p in ep.Parameters.Where(p => p.In == "path"))
                parts.Add($"{MapToCSharpType(p.Type ?? "string", false)} {p.Name}");

        if (ep.RequestBody?.Properties is { Count: > 0 })
            parts.Add($"{ep.OperationId?.Split('.').Last() ?? "Unknown"}Request request");

        return parts.Count > 0 ? string.Join(", ", parts) + ", " : "";
    }

    private static string SimplifyType(string fqn)
    {
        var simple = fqn.Replace("global::", "").Replace("System.", "");
        return simple switch
        {
            "Guid" => "Guid",
            "String" or "string" => "string",
            "Int32" or "int" => "int",
            "Boolean" or "bool" => "bool",
            "Decimal" or "decimal" => "decimal",
            _ => simple.Contains('.') ? simple[(simple.LastIndexOf('.') + 1)..] : simple
        };
    }

    private static string MapToCSharpType(string type, bool isNullable)
    {
        var baseType = SimplifyType(type);
        return isNullable ? $"{baseType}?" : baseType;
    }
}
