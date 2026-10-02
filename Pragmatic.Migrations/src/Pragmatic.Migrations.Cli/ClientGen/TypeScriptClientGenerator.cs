using System.Text;

namespace Pragmatic.Migrations.Cli.ClientGen;

/// <summary>
///     Generates a TypeScript client package from manifest data.
///     Produces: interface, fetch-based HTTP client, model types, error types, barrel exports.
/// </summary>
public sealed class TypeScriptClientGenerator(ClientManifest manifest, string packageName, string outputDir)
{
    // Known type names from this manifest — used to resolve property types
    private readonly HashSet<string> _knownDtoNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownEnumNames = new(StringComparer.Ordinal);

    public void Generate()
    {
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(Path.Combine(outputDir, "src"));

        // Pre-collect known types for cross-reference resolution
        if (manifest.Types is not null)
        {
            foreach (var t in manifest.Types)
            {
                if (t.SimpleName is null) continue;
                if (t.Kind == "enum") _knownEnumNames.Add(t.SimpleName);
                else if (t.Kind is "entity" or "dto") _knownDtoNames.Add(
                    t.SimpleName.EndsWith("Dto", StringComparison.Ordinal) ? t.SimpleName : t.SimpleName + "Dto");
            }
        }

        GeneratePackageJson();
        GenerateTsConfig();
        GenerateTypes();
        GenerateClient();
        GenerateBarrelExport();
    }

    private void GeneratePackageJson()
    {
        var content = $$"""
            {
              "name": "{{packageName}}",
              "version": "1.0.0",
              "type": "module",
              "main": "./src/index.ts",
              "types": "./src/index.ts",
              "scripts": {
                "build": "tsc",
                "typecheck": "tsc --noEmit"
              },
              "devDependencies": {
                "typescript": "^5.7.0"
              }
            }
            """;
        File.WriteAllText(Path.Combine(outputDir, "package.json"), content);
    }

    private void GenerateTsConfig()
    {
        var content = """
            {
              "compilerOptions": {
                "target": "ES2022",
                "module": "ESNext",
                "moduleResolution": "bundler",
                "strict": true,
                "declaration": true,
                "outDir": "./dist",
                "rootDir": "./src"
              },
              "include": ["src"]
            }
            """;
        File.WriteAllText(Path.Combine(outputDir, "tsconfig.json"), content);
    }

    private void GenerateTypes()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated from Pragmatic manifest — do not edit");
        sb.AppendLine();

        // Result type (lightweight, no external dep)
        sb.AppendLine("export type Result<T, E> = { ok: true; value: T } | { ok: false; error: E };");
        sb.AppendLine("export type VoidResult<E> = { ok: true } | { ok: false; error: E };");
        sb.AppendLine();

        // Error interface
        sb.AppendLine("export interface ApiError {");
        sb.AppendLine("  code: string;");
        sb.AppendLine("  statusCode: number;");
        sb.AppendLine("  title: string;");
        sb.AppendLine("  detail?: string;");
        sb.AppendLine("  [key: string]: unknown;");
        sb.AppendLine("}");
        sb.AppendLine();

        // Enums
        if (manifest.Types is not null)
        {
            foreach (var type in manifest.Types.Where(t => t.Kind == "enum" && t.EnumValues is { Count: > 0 }))
            {
                sb.AppendLine($"export type {type.SimpleName} = {string.Join(" | ", type.EnumValues!.Select(v => $"'{v}'"))};");
                sb.AppendLine();
            }
        }

        // Entity/DTO types
        if (manifest.Types is not null)
        {
            var generated = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in manifest.Types
                .Where(t => (t.Kind == "entity" || t.Kind == "dto") && t.Properties is { Count: > 0 })
                .OrderBy(t => t.Kind == "dto" ? 0 : 1))
            {
                var tsName = type.SimpleName!.EndsWith("Dto", StringComparison.Ordinal)
                    ? type.SimpleName : type.SimpleName + "Dto";
                if (!generated.Add(tsName)) continue;

                sb.AppendLine($"export interface {tsName} {{");
                foreach (var p in type.Properties!)
                {
                    var tsType = MapToTsType(p.Type, p.IsNullable, p.IsEnum);
                    var optional = p.IsNullable ? "?" : "";
                    sb.AppendLine($"  {ToCamelCase(p.Name ?? "unknown")}{optional}: {tsType};");
                }
                sb.AppendLine("}");
                sb.AppendLine();
            }
        }

        // Error types
        if (manifest.Types is not null)
        {
            foreach (var type in manifest.Types.Where(t => t.Kind == "error"))
            {
                sb.AppendLine($"export interface {type.SimpleName} extends ApiError {{");
                sb.AppendLine($"  code: '{type.ErrorCode}';");
                sb.AppendLine($"  statusCode: {type.ErrorStatusCode ?? 500};");

                if (type.Extensions is { Count: > 0 })
                {
                    foreach (var ext in type.Extensions)
                    {
                        if (ext.Name is null) continue;
                        sb.AppendLine($"  {ext.Name}?: {MapToTsType(ext.Type, true)};");
                    }
                }

                sb.AppendLine("}");
                sb.AppendLine();
            }
        }

        // Request DTOs
        if (manifest.Endpoints is not null)
        {
            var generated = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ep in manifest.Endpoints)
            {
                if (ep.RequestBody?.Properties is not { Count: > 0 }) continue;
                var reqName = (ep.OperationId?.Split('.').Last() ?? "Unknown") + "Request";
                if (!generated.Add(reqName)) continue;

                sb.AppendLine($"export interface {reqName} {{");
                foreach (var p in ep.RequestBody.Properties)
                {
                    if (p.Name is null) continue;
                    var tsType = MapToTsType(p.Type, p.IsNullable, p.IsEnum);
                    var optional = p.IsRequired ? "" : "?";
                    sb.AppendLine($"  {ToCamelCase(p.Name)}{optional}: {tsType};");
                }
                sb.AppendLine("}");
                sb.AppendLine();
            }
        }

        File.WriteAllText(Path.Combine(outputDir, "src", "types.ts"), sb.ToString());
    }

    private void GenerateClient()
    {
        if (manifest.Endpoints is not { Count: > 0 }) return;

        var boundary = GetBoundaryName();
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated from Pragmatic manifest — do not edit");
        sb.AppendLine("import type { Result, VoidResult, ApiError } from './types';");

        // Collect needed type imports
        var typeImports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ep in manifest.Endpoints)
        {
            if (ep.RequestBody?.Properties is { Count: > 0 })
                typeImports.Add((ep.OperationId?.Split('.').Last() ?? "Unknown") + "Request");

            var returnTs = GetReturnTsType(ep);
            if (returnTs is not null && returnTs != "void" && !IsPrimitiveTsType(returnTs))
            {
                // Strip array suffix for import — AvailableRoomResultDto[] → AvailableRoomResultDto
                var importName = returnTs.EndsWith("[]") ? returnTs.Substring(0, returnTs.Length - 2) : returnTs;
                if (!IsPrimitiveTsType(importName))
                    typeImports.Add(importName);
            }
        }

        if (typeImports.Count > 0)
            sb.AppendLine($"import type {{ {string.Join(", ", typeImports.OrderBy(x => x))} }} from './types';");
        sb.AppendLine();

        // Interface
        sb.AppendLine($"export interface I{boundary}Client {{");
        foreach (var ep in manifest.Endpoints)
        {
            var method = ep.OperationId?.Split('.').Last() ?? "unknown";
            var returnType = GetResultType(ep);
            var @params = BuildTsParams(ep);

            if (ep.Summary is not null)
                sb.AppendLine($"  /** {ep.Summary} */");
            sb.AppendLine($"  {ToCamelCase(method)}({@params}): Promise<{returnType}>;");
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // Implementation
        sb.AppendLine($"export function create{boundary}Client(baseUrl: string, fetchFn: typeof fetch = fetch): I{boundary}Client {{");
        sb.AppendLine("  const headers = { 'Content-Type': 'application/json' };");
        sb.AppendLine();
        sb.AppendLine("  async function mapError(response: Response): Promise<ApiError> {");
        sb.AppendLine("    try {");
        sb.AppendLine("      const body = await response.json();");
        sb.AppendLine("      return { code: body.code ?? 'UNKNOWN', statusCode: response.status, title: body.title ?? 'Error', ...body };");
        sb.AppendLine("    } catch {");
        sb.AppendLine("      return { code: 'UNKNOWN', statusCode: response.status, title: 'Request failed' };");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine("  return {");

        foreach (var ep in manifest.Endpoints)
        {
            var method = ToCamelCase(ep.OperationId?.Split('.').Last() ?? "unknown");
            var httpMethod = (ep.HttpMethod ?? "GET").ToUpperInvariant();
            var route = ep.FullRoute ?? "/";
            var @params = BuildTsParams(ep);
            var hasBody = ep.RequestBody?.Properties is { Count: > 0 };

            sb.AppendLine($"    async {method}({@params}) {{");

            // Route interpolation (path params)
            var routeExpr = $"`${{baseUrl}}{route}`";
            if (ep.Parameters is { Count: > 0 })
            {
                foreach (var p in ep.Parameters.Where(p => p.In == "path"))
                    routeExpr = routeExpr.Replace($"{{{p.Name}}}", $"${{{ToCamelCase(p.Name ?? "arg")}}}");
            }

            // Build the request URL, appending any query-string parameters.
            var queryParams = ep.Parameters?.Where(p => p.In == "query" && p.Name is not null).ToList();
            if (queryParams is { Count: > 0 })
            {
                sb.AppendLine($"      const url = new URL({routeExpr});");
                foreach (var p in queryParams)
                {
                    var jsName = ToCamelCase(p.Name!);
                    // Only append the parameter when a value was supplied (skip null/undefined).
                    sb.AppendLine($"      if ({jsName} !== undefined && {jsName} !== null) url.searchParams.set('{p.Name}', String({jsName}));");
                }
                routeExpr = "url";
            }

            var fetchOpts = httpMethod == "GET"
                ? "{ headers }"
                : hasBody
                    ? $"{{ method: '{httpMethod}', headers, body: JSON.stringify(request) }}"
                    : $"{{ method: '{httpMethod}', headers }}";

            sb.AppendLine($"      const response = await fetchFn({routeExpr}, {fetchOpts});");
            sb.AppendLine("      if (!response.ok) return { ok: false, error: await mapError(response) };");

            if (ep.IsVoid)
            {
                sb.AppendLine("      return { ok: true };");
            }
            else
            {
                sb.AppendLine("      return { ok: true, value: await response.json() };");
            }

            sb.AppendLine("    },");
        }

        sb.AppendLine("  };");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(outputDir, "src", "client.ts"), sb.ToString());
    }

    private void GenerateBarrelExport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated barrel export");
        sb.AppendLine("export * from './types';");
        sb.AppendLine("export * from './client';");
        File.WriteAllText(Path.Combine(outputDir, "src", "index.ts"), sb.ToString());
    }

    // Helpers

    private string GetBoundaryName()
    {
        var assembly = manifest.Assembly ?? "App";
        var parts = assembly.Split('.');
        return parts.Length >= 2 ? parts[^1] : parts[0];
    }

    private string GetReturnTsType(ClientEndpoint ep)
    {
        if (ep.IsVoid) return "void";
        var fqn = ep.Response?.Type ?? "object";
        return SimplifyToTsType(fqn);
    }

    private string GetResultType(ClientEndpoint ep)
    {
        if (ep.IsVoid) return "VoidResult<ApiError>";
        var tsType = GetReturnTsType(ep);
        return $"Result<{tsType}, ApiError>";
    }

    private string BuildTsParams(ClientEndpoint ep)
    {
        var parts = new List<string>();
        if (ep.Parameters is { Count: > 0 })
        {
            foreach (var p in ep.Parameters.Where(p => p.In == "path"))
                parts.Add($"{ToCamelCase(p.Name ?? "arg")}: {MapToTsType(p.Type, false)}");

            // Query params: optional when not required (trailing optional params are valid TS).
            foreach (var p in ep.Parameters.Where(p => p.In == "query" && p.Name is not null))
            {
                var optional = p.IsRequired ? "" : "?";
                parts.Add($"{ToCamelCase(p.Name!)}{optional}: {MapToTsType(p.Type, false)}");
            }
        }
        if (ep.RequestBody?.Properties is { Count: > 0 })
        {
            var reqName = (ep.OperationId?.Split('.').Last() ?? "Unknown") + "Request";
            parts.Add($"request: {reqName}");
        }
        return string.Join(", ", parts);
    }

    private string MapToTsType(string? typeFqn, bool isNullable, bool isEnum = false)
    {
        var ts = isEnum ? ResolveEnumType(typeFqn) : SimplifyToTsType(typeFqn ?? "any");
        return isNullable ? $"{ts} | null" : ts;
    }

    private string ResolveEnumType(string? typeFqn)
    {
        if (typeFqn is null) return "string";
        var simple = ExtractSimpleName(typeFqn);
        return _knownEnumNames.Contains(simple) ? simple : "string";
    }

    private string SimplifyToTsType(string fqn)
    {
        var clean = fqn.Replace("global::", "").Replace("System.", "").TrimEnd('?');

        // Array type: T[] → SimplifyToTsType(T)[]
        if (clean.EndsWith("[]"))
        {
            var elementType = SimplifyToTsType(clean.Substring(0, clean.Length - 2));
            return $"{elementType}[]";
        }

        if (clean.Contains('<'))
            return "unknown";

        var simple = ExtractSimpleName(clean);
        return simple switch
        {
            "Guid" => "string",
            "String" or "string" => "string",
            "Int32" or "int" or "Int64" or "long" or "Byte" or "byte"
                or "Decimal" or "decimal" or "Double" or "double" or "Float" or "float" => "number",
            "Boolean" or "bool" => "boolean",
            "DateTimeOffset" or "DateTime" or "DateOnly" or "TimeOnly" => "string",
            "Object" or "object" => "unknown",
            // Known enum → use as-is (no Dto suffix)
            _ when _knownEnumNames.Contains(simple) => simple,
            // Known DTO → use as-is
            _ when simple.EndsWith("Dto", StringComparison.Ordinal) && _knownDtoNames.Contains(simple) => simple,
            // Known entity → append Dto
            _ when _knownDtoNames.Contains(simple + "Dto") => simple + "Dto",
            // Unknown type → unknown (don't invent names for cross-boundary types)
            _ => "unknown"
        };
    }

    private static string ExtractSimpleName(string fqn)
    {
        var clean = fqn.Replace("global::", "").Replace("System.", "").TrimEnd('?');
        return clean.Contains('.') ? clean.Substring(clean.LastIndexOf('.') + 1) : clean;
    }

    private static bool IsPrimitiveTsType(string tsType)
        => tsType is "string" or "number" or "boolean" or "unknown" or "void";

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
