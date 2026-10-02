using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     Enriches the OpenAPI document with Pragmatic manifest data:
///     typed error responses, permission metadata.
/// </summary>
public sealed class ManifestOpenApiTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ⚠️ This host's manifests, not the process's. The registry accumulates across hosts, so with
        // two in one process the lookup below was built from both — see HostManifest.
        var manifests = ManifestReader.ReadFor(context.ApplicationServices);

        // The document paths reflect runtime routing, which includes the configured
        // PragmaticEndpointsOptions.RoutePrefix; the manifest routes are compile-time and do not.
        // Prepend the runtime prefix to manifest routes so a prefixed app still matches (otherwise
        // enrichment is silently skipped and the generated document diverges from the live API).
        var routePrefix = ResolveRoutePrefix(context);
        var endpointLookup = BuildEndpointLookup(manifests, routePrefix);
        if (endpointLookup.Count == 0)
            return Task.CompletedTask;

        if (document.Paths is null)
            return Task.CompletedTask;

        // The schemes the application described, and only those — the same rule as the compile-time
        // document (OpenApiSecuritySchemes). Declared once, so each requirement has one to reference.
        var schemes = OpenApiSecuritySchemes.Describe(
            context.ApplicationServices,
            requiresAuthentication: endpointLookup.Values.Any(e => e.Authorization is { AllowAnonymous: false }));
        AddSecuritySchemes(document, schemes);

        foreach (var pathItem in document.Paths)
        {
            if (pathItem.Value.Operations is null) continue;
            foreach (var operation in pathItem.Value.Operations)
            {
                var httpMethod = operation.Key.ToString().ToUpperInvariant();
                var route = NormalizeRoute(pathItem.Key);

                if (!TryFindEndpoint(endpointLookup, httpMethod, route, out var endpoint))
                    continue;

                EnrichOperation(operation.Value, endpoint, document, schemes);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Declares the contributed schemes under <c>components.securitySchemes</c>. Never overwrites a
    ///     same-named scheme the application added to the document itself.
    /// </summary>
    internal static void AddSecuritySchemes(
        OpenApiDocument document, IReadOnlyList<Abstractions.Http.OpenApiSecurityScheme> schemes)
    {
        if (schemes.Count == 0)
            return;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        foreach (var scheme in schemes)
            document.Components.SecuritySchemes.TryAdd(scheme.Name, OpenApiSecuritySchemes.ToDocumentScheme(scheme));
    }

    internal static void EnrichOperation(
        OpenApiOperation operation,
        ManifestEndpoint endpoint,
        OpenApiDocument document,
        IReadOnlyList<Abstractions.Http.OpenApiSecurityScheme> schemes)
    {
        // Merge manifest tags into the operation's tag set (case-insensitive, no duplicates;
        // ASP.NET may already have added some via WithTags).
        if (endpoint.Tags is { Count: > 0 } tags)
        {
            operation.Tags ??= new HashSet<OpenApiTagReference>();
            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                if (operation.Tags.Any(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase)))
                    continue;
                operation.Tags.Add(new OpenApiTagReference(tag, document));
            }
        }

        // Add error responses not already present, each with a machine-readable problem+json schema.
        if (endpoint.Errors is { Count: > 0 })
        {
            operation.Responses ??= new OpenApiResponses();
            foreach (var error in endpoint.Errors)
            {
                var statusStr = error.StatusCode.ToString();
                if (operation.Responses.ContainsKey(statusStr)) continue;
                var response = new OpenApiResponse
                {
                    Description = error.Code ?? $"Error {error.StatusCode}",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/problem+json"] = new() { Schema = BuildProblemSchema(error) }
                    }
                };
                operation.Responses[statusStr] = response;
            }
        }

        // Machine-readable security requirement (AllowAnonymous-aware). The human-readable
        // "Requires permissions: ..." line below is kept in addition, not replaced.
        if (endpoint.Authorization is { } auth)
            ApplySecurity(operation, auth, document, schemes);

        // Add summary with permission info.
        // This must run independently of error responses: behind an early return for endpoints
        // with no documented errors, their permissions would be omitted.
        if (endpoint.Authorization?.RequiredPermissions is { Count: > 0 } permissions)
        {
            var permStr = string.Join(", ", permissions);
            AppendDescription(operation, $"Requires permissions: {permStr}");
        }

        // Reflect deprecation, rate limiting, response cache, and file-upload constraints.
        if (endpoint.IsDeprecated)
            operation.Deprecated = true;

        if (!string.IsNullOrEmpty(endpoint.RateLimitPolicy))
            AppendDescription(operation, $"Rate limit policy: {endpoint.RateLimitPolicy}");

        if (endpoint.CacheDurationSeconds is > 0)
            AppendDescription(operation, $"Cached for {endpoint.CacheDurationSeconds}s");

        if (endpoint.FileUpload is { } upload)
        {
            if (upload.MaxFileSizeBytes is > 0)
                AppendDescription(operation, $"Max upload size: {upload.MaxFileSizeBytes} bytes");
            if (upload.AllowedContentTypes is { Count: > 0 } types)
                AppendDescription(operation, $"Allowed content types: {string.Join(", ", types)}");
        }

        if (endpoint.MaxBodySizeBytes is > 0)
            AppendDescription(operation, $"Max request body size: {endpoint.MaxBodySizeBytes} bytes");

        // [Idempotent]: document the required idempotency-key header.
        if (endpoint.IdempotencyHeader is { Length: > 0 } idempotencyHeader)
        {
            operation.Parameters ??= [];
            if (!operation.Parameters.Any(p =>
                    string.Equals(p.Name, idempotencyHeader, StringComparison.OrdinalIgnoreCase) &&
                    p.In == ParameterLocation.Header))
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = idempotencyHeader,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "Idempotency key: retries with the same key replay the original 2xx response.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                });
        }

        // Description from [EndpointDescription] (the base document only carries the summary).
        if (operation.Description is null && !string.IsNullOrEmpty(endpoint.Description))
            operation.Description = endpoint.Description;

        ApplyParameterDocs(operation, endpoint);
        ApplyRequestExamples(operation, endpoint);
        ApplyResponseExamples(operation, endpoint);
    }

    private static void ApplyParameterDocs(OpenApiOperation operation, ManifestEndpoint endpoint)
    {
        if (endpoint.Parameters is not { Count: > 0 } manifestParams)
            return;

        foreach (var manifestParam in manifestParams)
        {
            if (manifestParam.Name is null) continue;

            // Cookie parameters are bound inside the generated handler body, so ASP.NET
            // cannot discover them — document them here from the manifest.
            if (string.Equals(manifestParam.In, "cookie", StringComparison.OrdinalIgnoreCase))
            {
                operation.Parameters ??= [];
                if (!operation.Parameters.Any(p =>
                        string.Equals(p.Name, manifestParam.Name, StringComparison.OrdinalIgnoreCase) &&
                        p.In == ParameterLocation.Cookie))
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = manifestParam.Name,
                        In = ParameterLocation.Cookie,
                        Required = manifestParam.IsRequired,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                    });
                continue;
            }

            // Parameter defaults captured from property initializers at compile time.
            if (manifestParam.DefaultValue is null || operation.Parameters is not { Count: > 0 }) continue;

            var operationParam = operation.Parameters.FirstOrDefault(p =>
                string.Equals(p.Name, manifestParam.Name, StringComparison.OrdinalIgnoreCase));

            if (operationParam?.Schema is OpenApiSchema { Default: null } schema)
                schema.Default = ToJsonNode(manifestParam.DefaultValue, manifestParam.Type);
        }
    }

    private static void ApplyRequestExamples(OpenApiOperation operation, ManifestEndpoint endpoint)
    {
        if (endpoint.RequestExamples is not { Count: > 0 } examples)
            return;

        if (operation.RequestBody?.Content?.TryGetValue("application/json", out var media) is not true || media is null)
            return;

        // OpenAPI: Example and Examples are mutually exclusive — use the map when > 1.
        if (examples.Count == 1 && examples[0].Name is null)
        {
            media.Example = TryParseJson(examples[0].Json);
            return;
        }

        media.Examples ??= new Dictionary<string, IOpenApiExample>();
        for (var i = 0; i < examples.Count; i++)
        {
            var example = examples[i];
            var key = example.Name ?? $"example{i + 1}";
            media.Examples[key] = new OpenApiExample
            {
                Summary = example.Summary,
                Value = TryParseJson(example.Json)
            };
        }
    }

    private static void ApplyResponseExamples(OpenApiOperation operation, ManifestEndpoint endpoint)
    {
        if (endpoint.ResponseExamples is not { Count: > 0 } examples)
            return;

        foreach (var group in examples.GroupBy(e => e.StatusCode))
        {
            operation.Responses ??= new OpenApiResponses();
            var statusKey = group.Key.ToString();
            if (!operation.Responses.TryGetValue(statusKey, out var response) || response is not OpenApiResponse concrete)
            {
                concrete = new OpenApiResponse { Description = $"Status {statusKey}" };
                operation.Responses[statusKey] = concrete;
            }

            concrete.Content ??= new Dictionary<string, OpenApiMediaType>();
            if (!concrete.Content.TryGetValue("application/json", out var media) || media is null)
            {
                media = new OpenApiMediaType();
                concrete.Content["application/json"] = media;
            }

            var groupList = group.ToList();
            if (groupList.Count == 1 && groupList[0].Name is null)
            {
                media.Example = TryParseJson(groupList[0].Json);
                continue;
            }

            media.Examples ??= new Dictionary<string, IOpenApiExample>();
            for (var i = 0; i < groupList.Count; i++)
            {
                var example = groupList[i];
                var key = example.Name ?? $"example{i + 1}";
                media.Examples[key] = new OpenApiExample { Value = TryParseJson(example.Json) };
            }
        }
    }

    private static JsonNode? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            // PRAG0518 already warned at compile time; fall back to the raw string.
            return JsonValue.Create(json);
        }
    }

    /// <summary>Converts the manifest default (string form) to a typed JSON node for the schema.</summary>
    private static JsonNode ToJsonNode(string value, string? type)
    {
        if (bool.TryParse(value, out var boolValue))
            return JsonValue.Create(boolValue);

        var isNumericType = type is not null &&
                            (type.Contains("int", StringComparison.OrdinalIgnoreCase) ||
                             type.Contains("long") || type.Contains("short") ||
                             type.Contains("double") || type.Contains("float") ||
                             type.Contains("decimal"));
        if (isNumericType && decimal.TryParse(value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var numberValue))
            return JsonValue.Create(numberValue);

        return JsonValue.Create(value);
    }

    /// <summary>
    ///     Applies the operation-level security. Anonymous endpoints get an explicit empty
    ///     requirement list (neutralizing any document-global requirement); authenticated ones get
    ///     one requirement naming every contributed scheme — all of them apply, as in the compile-time
    ///     document — and nothing at all when no scheme was contributed.
    /// </summary>
    private static void ApplySecurity(
        OpenApiOperation operation,
        ManifestAuth auth,
        OpenApiDocument document,
        IReadOnlyList<Abstractions.Http.OpenApiSecurityScheme> schemes)
    {
        operation.Security ??= new List<OpenApiSecurityRequirement>();

        if (auth.AllowAnonymous)
        {
            // Public operation: an empty Security array means "no security required" and overrides
            // any global default the app may declare.
            operation.Security.Clear();
            return;
        }

        // A requirement has to name a scheme someone described; with none, the operation says nothing
        // rather than naming an invented one, and OpenApiSecuritySchemes has logged the gap.
        if (schemes.Count == 0)
            return;

        var names = schemes.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        if (operation.Security.Any(req => req.Keys.Any(k => k.Reference?.Id is { } id && names.Contains(id))))
            return;

        var requirement = new OpenApiSecurityRequirement();
        foreach (var scheme in schemes)
            requirement[new OpenApiSecuritySchemeReference(scheme.Name, document)] = [];

        operation.Security.Add(requirement);
    }

    /// <summary>Builds an RFC 7807 ProblemDetails schema, enriched with the error's extensions.</summary>
    private static OpenApiSchema BuildProblemSchema(ManifestError error)
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "RFC 7807 Problem Details for HTTP APIs",
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                ["code"] = new OpenApiSchema { Type = JsonSchemaType.String }
            },
            AdditionalPropertiesAllowed = true
        };

        if (error.Extensions is { Count: > 0 } extensions)
        {
            foreach (var ext in extensions)
            {
                if (string.IsNullOrEmpty(ext.Name) || schema.Properties.ContainsKey(ext.Name))
                    continue;
                schema.Properties[ext.Name] = new OpenApiSchema { Type = MapExtensionType(ext.Type) };
            }
        }

        return schema;
    }

    /// <summary>Maps a manifest extension CLR type name to a JSON schema type (defaults to string).</summary>
    private static JsonSchemaType MapExtensionType(string? type)
    {
        if (type is null) return JsonSchemaType.String;
        if (type.Contains("bool", StringComparison.OrdinalIgnoreCase)) return JsonSchemaType.Boolean;
        if (type.Contains("int", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("long", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("short", StringComparison.OrdinalIgnoreCase)) return JsonSchemaType.Integer;
        if (type.Contains("double", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("float", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("decimal", StringComparison.OrdinalIgnoreCase)) return JsonSchemaType.Number;
        return JsonSchemaType.String;
    }

    private static void AppendDescription(OpenApiOperation operation, string line)
    {
        operation.Description = string.IsNullOrEmpty(operation.Description)
            ? line
            : $"{operation.Description}\n\n{line}";
    }

    internal static Dictionary<string, ManifestEndpoint> BuildEndpointLookup(
        IReadOnlyList<ManifestDocument> manifests, string routePrefix)
    {
        var lookup = new Dictionary<string, ManifestEndpoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in manifests)
        {
            if (manifest.Endpoints is null) continue;
            foreach (var ep in manifest.Endpoints)
            {
                if (ep.FullRoute is null || ep.HttpMethod is null) continue;
                var prefixedRoute = CombineRoutePrefix(routePrefix, ep.FullRoute);
                var key = $"{ep.HttpMethod.ToUpperInvariant()}:{NormalizeRoute(prefixedRoute)}";
                lookup.TryAdd(key, ep);
            }
        }
        return lookup;
    }

    private static string ResolveRoutePrefix(OpenApiDocumentTransformerContext context)
    {
        var options = context.ApplicationServices
                          .GetService(typeof(global::Pragmatic.Endpoints.Configuration.PragmaticEndpointsOptions))
                      as global::Pragmatic.Endpoints.Configuration.PragmaticEndpointsOptions;
        return options?.RoutePrefix ?? string.Empty;
    }

    internal static string CombineRoutePrefix(string prefix, string route)
        => string.IsNullOrEmpty(prefix)
            ? route
            : $"{prefix.TrimEnd('/')}/{route.TrimStart('/')}";

    private static bool TryFindEndpoint(
        Dictionary<string, ManifestEndpoint> lookup, string method, string route,
        out ManifestEndpoint endpoint)
    {
        return lookup.TryGetValue($"{method}:{route}", out endpoint!);
    }

    private static string NormalizeRoute(string route)
    {
        // Trim leading '/' and lowercase, then collapse every "{...}" segment to a
        // bare "{}". Without this last step a manifest route "/users/{id}" would fail
        // to match an endpoint declared as "/users/{userId}" because the parameter
        // NAMES differ even though both express the same shape — the manifest
        // lookup is shape-based, not name-based.
        return RouteParamRegex.Replace(route.TrimStart('/').ToLowerInvariant(), "{}");
    }

    private static readonly System.Text.RegularExpressions.Regex RouteParamRegex =
        new(@"\{[^{}]+\}", System.Text.RegularExpressions.RegexOptions.Compiled);
}
