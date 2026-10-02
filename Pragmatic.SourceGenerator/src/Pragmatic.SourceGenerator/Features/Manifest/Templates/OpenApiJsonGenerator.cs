using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Manifest.Templates;

/// <summary>
///     Generates an OpenAPI 3.1 JSON document from aggregated manifest JSON strings.
///     Includes: paths, operations, parameters, request/response schemas with $ref,
///     enum values, validation constraints (maxLength, required), error schemas,
///     and x-pragmatic-permissions extensions.
/// </summary>
internal static partial class OpenApiJsonGenerator
{
    private const string ProblemDetailsSchema = "ProblemDetails";


    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    ///     The document, and whether anything in it requires authentication.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A returned value, never a static field. Generators run in parallel and the incremental
    ///     pipeline compares what a step returns; a fact parked in static state is read by whoever
    ///     asks next, which on a second compilation is someone else.
    ///     <para>
    ///         The mount needs the flag to tell two silences apart: an application where nothing
    ///         requires authentication, complete as it stands, and one where something does and no
    ///         scheme was registered to describe it — a gap worth saying out loud rather than
    ///         filling with a guess.
    ///     </para>
    /// </remarks>
    public readonly struct OpenApiDocument
    {
        public OpenApiDocument(string? json, bool requiresAuthentication)
        {
            Json = json;
            RequiresAuthentication = requiresAuthentication;
        }

        /// <summary>The OpenAPI 3.1 JSON, or null when there was nothing to describe.</summary>
        public string? Json { get; }

        /// <summary>True when at least one operation is not <c>[AllowAnonymous]</c>.</summary>
        public bool RequiresAuthentication { get; }
    }

    /// <summary>
    ///     The contract of an application with no operations: a valid document whose <c>paths</c> is
    ///     empty.
    /// </summary>
    /// <remarks>
    ///     <see cref="Generate" /> still answers "nothing to describe" with no document; whether that
    ///     silence should be published as an empty contract is the host's call, because only the host
    ///     knows it can serve one.
    /// </remarks>
    public static string WithNoOperations(string assemblyName)
    {
        var b = new MetadataJsonBuilder(indent: true);
        b.StartObject();
        b.Property("openapi").Value("3.1.0");
        b.Property("info").StartObject();
        b.Property("title").Value(assemblyName);
        b.Property("version").Value("1.0.0");
        b.Property("description").Value("Compile-time OpenAPI from Pragmatic Manifest (0 operations)");
        b.EndObject();
        b.Property("paths").StartObject();
        b.EndObject();
        b.EndObject();
        return b.ToString();
    }

    public static OpenApiDocument Generate(string assemblyName, IReadOnlyList<string> manifestJsons)
    {
        if (manifestJsons.Count == 0) return default;

        var allEndpoints = new List<EndpointDto>();
        var allTypes = new List<TypeDto>();

        foreach (var json in manifestJsons)
        {
            try
            {
                var doc = JsonSerializer.Deserialize<ManifestDocDto>(json, JsonOpts);
                if (doc?.Endpoints is not null) allEndpoints.AddRange(doc.Endpoints);
                if (doc?.Types is not null) allTypes.AddRange(doc.Types);
            }
            catch { /* Skip malformed */ }
        }

        if (allEndpoints.Count == 0) return default;

        var requiresAuthentication =
            allEndpoints.Any(e => e.Authorization is not { AllowAnonymous: true });

        // One authority for "which components.schemas key does this type get", shared by the $ref sites
        // and by the schema rendering — deciding independently, the two could disagree.
        var catalog = SchemaCatalog.Build(allTypes, allEndpoints);

        var b = new MetadataJsonBuilder(indent: true);
        b.StartObject();

        b.Property("openapi").Value("3.1.0");
        b.Property("info").StartObject();
        b.Property("title").Value(assemblyName);
        b.Property("version").Value("1.0.0");
        b.Property("description").Value($"Compile-time OpenAPI from Pragmatic Manifest ({allEndpoints.Count} operations)");
        b.EndObject();

        // Paths
        b.Property("paths").StartObject();
        var pathGroups = allEndpoints
            .Where(e => e.FullRoute is not null && e.HttpMethod is not null)
            .GroupBy(e => e.FullRoute!);

        foreach (var pathGroup in pathGroups)
        {
            b.Property(pathGroup.Key).StartObject();
            foreach (var ep in pathGroup)
                RenderOperation(b, ep, catalog);
            b.EndObject();
        }
        b.EndObject(); // paths

        // Components. Paths are rendered first on purpose: resolving the responses is what discovers
        // which paged envelopes have to exist.
        b.Property("components").StartObject();
        b.Property("schemas").StartObject();
        RenderProblemDetailsSchema(b);

        // Only what a caller can reach. Computed after the paths for the same reason they come first:
        // resolving the responses is what creates the paged envelopes, and an envelope discovered
        // afterwards would not be in the set.
        RenderTypeSchemas(b, catalog, catalog.Reachable(allEndpoints));
        RenderPagedEnvelopeSchemas(b, catalog);
        RenderRequestSchemas(b, allEndpoints, catalog);
        b.EndObject(); // schemas
        b.EndObject(); // components

        // ⚠️ No securitySchemes here, and it is not an omission. WHICH operations need
        // authentication is settled at compile time — [AllowAnonymous], and the empty security array
        // each anonymous operation publishes. WHAT the requirement looks like on the wire is chosen
        // in Program.cs, where this generator cannot see: the mount composes it from what the
        // authentication itself registered. See RequiresAuthentication, which is the half this side
        // does know.

        b.EndObject();
        return new OpenApiDocument(b.ToString(), requiresAuthentication);
    }

    private static void RenderOperation(MetadataJsonBuilder b, EndpointDto ep, SchemaCatalog catalog)
    {
        var method = ep.HttpMethod!.ToLowerInvariant();
        b.Property(method).StartObject();

        if (ep.OperationId is not null) b.Property("operationId").Value(ep.OperationId);
        if (ep.Summary is not null) b.Property("summary").Value(ep.Summary);
        if (ep.Description is not null) b.Property("description").Value(ep.Description);

        // ⚠️ [Tags] first, and the boundary only where nobody declared any. The manifest carries
        // the declared tags; publishing every operation under its boundary would give an author who
        // wrote [ApiTags("Invoices")] "Billing", with no way to notice — tags are the sections a generated client is organised into, and
        // being organised by the wrong name looks exactly like being organised.
        var declared = ep.Tags is { Count: > 0 } ? ep.Tags : null;
        var fallback = ep.OperationId?.Split('.').FirstOrDefault();

        if (declared is not null || fallback is not null)
        {
            b.Property("tags").StartArray();
            foreach (var tag in declared ?? [fallback!])
                b.Value(tag);
            b.EndArray();
        }

        // Parameters
        if (ep.Parameters is { Count: > 0 })
        {
            b.Property("parameters").StartArray();
            foreach (var p in ep.Parameters)
            {
                b.StartObject();
                b.Property("name").Value(p.Name ?? "");
                b.Property("in").Value(p.In ?? "query");
                b.Property("required").Value(p.IsRequired);
                b.Property("schema").StartObject();
                // The same renderer the schemas use: a query parameter can be an array — the framework
                // documents `?names=a&names=b` — and it was published as a string like everything else
                // that was not one of seven primitives.
                RenderPropertyType(b, catalog, p.Type, isEnum: false);
                if (p.DefaultValue is not null)
                    RenderSchemaDefault(b, MapJsonSchemaType(p.Type), p.DefaultValue);
                b.EndObject();
                b.EndObject();
            }
            b.EndArray();
        }

        // Request body with $ref
        if (ep.RequestBody?.Properties is { Count: > 0 })
        {
            // Bound directly: the schema is the property's own type. Naming a wrapper here would
            // publish a shape the endpoint refuses, which is worse than the wrapper it replaced.
            var reqSchemaName = DirectBodySchemaKey(catalog, ep) ?? RequestSchemaName(ep);
            b.Property("requestBody").StartObject();
            b.Property("required").Value(true);
            b.Property("content").StartObject();
            b.Property("application/json").StartObject();
            b.Property("schema").StartObject();
            b.Property("$ref").Value($"#/components/schemas/{reqSchemaName}");
            b.EndObject();
            RenderExamples(b, ep.RequestExamples);
            b.EndObject();
            b.EndObject();
            b.EndObject();
        }

        // Responses
        b.Property("responses").StartObject();
        var successCode = ep.SuccessStatusCode > 0 ? ep.SuccessStatusCode : 200;
        b.Property(successCode.ToString()).StartObject();
        if (ep.IsVoid)
        {
            b.Property("description").Value("No content");
        }
        else
        {
            b.Property("description").Value("Success");
            var responseSchema = catalog.ResolveResponse(ep.Response?.Type);
            var successExamples = ep.ResponseExamples?.Where(e => e.StatusCode == successCode).ToList();
            if (responseSchema is not null || successExamples is { Count: > 0 })
            {
                b.Property("content").StartObject();
                b.Property("application/json").StartObject();
                if (responseSchema is { } schema)
                {
                    b.Property("schema").StartObject();
                    if (schema.IsArray)
                    {
                        b.Property("type").Value("array");
                        b.Property("items").StartObject();
                        b.Property("$ref").Value($"#/components/schemas/{schema.Name}");
                        b.EndObject();
                    }
                    else
                    {
                        b.Property("$ref").Value($"#/components/schemas/{schema.Name}");
                    }
                    b.EndObject();
                }
                RenderExamples(b, successExamples?.Select(e => new ExampleDto
                {
                    Name = e.Name, Json = e.Json
                }).ToList());
                b.EndObject();
                b.EndObject();
            }
        }
        b.EndObject();

        // The declared error union first — it names the error, so its description is worth more — then
        // whatever the pipeline adds. Written once per status: a duplicate key is not valid JSON, and
        // the two lists overlap whenever a mutation declares an error the pipeline also produces.
        var written = new HashSet<int>();
        if (ep.Errors is { Count: > 0 })
        {
            foreach (var err in ep.Errors)
            {
                if (!written.Add(err.StatusCode))
                    continue;

                RenderProblemResponse(b, err.StatusCode, err.Code ?? "Error");
            }
        }

        if (ep.ProblemStatusCodes is { Count: > 0 })
        {
            foreach (var code in ep.ProblemStatusCodes)
            {
                if (!written.Add(code))
                    continue;

                RenderProblemResponse(b, code, DescribeProblem(code));
            }
        }

        b.EndObject(); // responses

        // An empty security array is how OpenAPI says "this one needs nothing", and it is the half
        // that makes the document-level requirement usable: without it every anonymous operation
        // would inherit a token the caller does not have, and a generated client would refuse to
        // call the login endpoint without being logged in.
        if (ep.Authorization is { AllowAnonymous: true })
        {
            b.Property("security").StartArray();
            b.EndArray();
        }

        // x-pragmatic-permissions
        if (ep.Authorization?.RequiredPermissions is { Count: > 0 } perms)
        {
            b.Property("x-pragmatic-permissions").StartArray();
            foreach (var p in perms) b.Value(p);
            b.EndArray();
        }

        b.EndObject(); // method
    }

    // Helpers

    /// <summary>
    ///     Renders "example"/"examples" for a media-type object. Valid JSON payloads are
    ///     embedded raw; invalid ones (already flagged by PRAG0518) fall back to a string.
    /// </summary>
    private static void RenderExamples(MetadataJsonBuilder b, List<ExampleDto>? examples)
    {
        if (examples is not { Count: > 0 })
            return;

        if (examples.Count == 1 && examples[0].Name is null)
        {
            b.Property("example");
            RenderExampleValue(b, examples[0].Json);
            return;
        }

        b.Property("examples").StartObject();
        for (var i = 0; i < examples.Count; i++)
        {
            var example = examples[i];
            b.Property(example.Name ?? $"example{i + 1}").StartObject();
            if (example.Summary is not null) b.Property("summary").Value(example.Summary);
            b.Property("value");
            RenderExampleValue(b, example.Json);
            b.EndObject();
        }
        b.EndObject();
    }

    private static void RenderExampleValue(MetadataJsonBuilder b, string? json)
    {
        if (json is not null && Pragmatic.SourceGen.JsonValidator.IsValid(json))
            b.RawValue(json);
        else
            b.Value(json ?? "");
    }

    private static void RenderSchemaDefault(MetadataJsonBuilder b, string schemaType, string defaultValue)
    {
        b.Property("default");
        switch (schemaType)
        {
            case "integer" when long.TryParse(defaultValue, out var intValue):
                b.Value(intValue);
                break;
            // decimal.TryParse with NumberStyles.Number accepts group separators and surrounding
            // whitespace ("1,234", " 1.5 ") — parseable, but not a JSON number. The value comes from a
            // user-written [DefaultValue], so it is validated rather than trusted before going in raw.
            case "number" when decimal.TryParse(defaultValue,
                                   System.Globalization.NumberStyles.Number,
                                   System.Globalization.CultureInfo.InvariantCulture, out _)
                               && Pragmatic.SourceGen.JsonValidator.IsValid(defaultValue):
                b.RawValue(defaultValue);
                break;
            case "boolean" when bool.TryParse(defaultValue, out var boolValue):
                b.Value(boolValue);
                break;
            default:
                b.Value(defaultValue);
                break;
        }
    }

    /// <summary>
    ///     The schema key of a directly-bound body, or <c>null</c> when a wrapper is still needed.
    /// </summary>
    /// <remarks>
    ///     One predicate for the two decisions that must agree — the <c>$ref</c> and whether to emit
    ///     the wrapper schema — because when they disagree the document names a component that was
    ///     never written. That happened: a body whose single property is a collection binds directly
    ///     but has no schema of its own to point at, so it keeps its wrapper.
    /// </remarks>
    private static string? DirectBodySchemaKey(SchemaCatalog catalog, EndpointDto ep)
        => ep.RequestBody is { Direct: true, Properties.Count: 1 }
            ? catalog.ResolveSchema(ep.RequestBody.Properties[0].Type)
            : null;

    /// <summary>The schema key of an operation's request body — derived from the operation id.</summary>
    private static string RequestSchemaName(EndpointDto ep)
        => (ep.OperationId?.Split('.').LastOrDefault() ?? "Unknown") + "Request";

    private static string MapJsonSchemaType(string? clrType)
    {
        if (clrType is null) return "string";
        var simple = clrType.Replace("global::", "").Replace("System.", "");
        if (simple.Contains('.')) simple = simple.Substring(simple.LastIndexOf('.') + 1);
        return simple.ToLowerInvariant() switch
        {
            "guid" => "string",
            "string" => "string",
            "int" or "int32" or "int64" or "long" or "short" or "byte" => "integer",
            "bool" or "boolean" => "boolean",
            "decimal" or "double" or "float" or "single" => "number",
            "dateonly" or "datetime" or "datetimeoffset" or "timeonly" or "timespan" => "string",
            _ => "string"
        };
    }

    private static string? MapJsonSchemaFormat(string? clrType)
    {
        if (clrType is null) return null;
        var simple = clrType.Replace("global::", "").Replace("System.", "");
        if (simple.Contains('.')) simple = simple.Substring(simple.LastIndexOf('.') + 1);
        return simple.ToLowerInvariant() switch
        {
            "guid" => "uuid",
            "int" or "int32" => "int32",
            "int64" or "long" => "int64",
            "double" or "float" or "single" => "double",
            "decimal" => "decimal",
            "dateonly" or "datetime" => "date",
            "datetimeoffset" => "date-time",
            "timeonly" or "timespan" => "time",
            _ => null
        };
    }

    /// <summary>One problem response, as a ProblemDetails reference.</summary>
    private static void RenderProblemResponse(MetadataJsonBuilder b, int statusCode, string description)
    {
        b.Property(statusCode.ToString()).StartObject();
        b.Property("description").Value(description);
        b.Property("content").StartObject();
        b.Property("application/problem+json").StartObject();
        b.Property("schema").StartObject();
        b.Property("$ref").Value("#/components/schemas/ProblemDetails");
        b.EndObject();
        b.EndObject();
        b.EndObject();
        b.EndObject();
    }

    /// <summary>
    ///     What a pipeline status means, for a reader who has no error type to read the name off.
    /// </summary>
    private static string DescribeProblem(int statusCode) => statusCode switch
    {
        400 => "Invalid request",
        401 => "Not authenticated",
        403 => "Not permitted",
        404 => "Not found",
        409 => "Conflict",
        422 => "Unprocessable",
        500 => "Unexpected error",
        _ => "Error",
    };

    private static string ToCamelCase(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    // DTOs
    private sealed class ManifestDocDto
    {
        public List<EndpointDto>? Endpoints { get; set; }
        public List<TypeDto>? Types { get; set; }
    }

    private sealed class EndpointDto
    {
        public string? OperationId { get; set; }
        public string? HttpMethod { get; set; }
        public string? FullRoute { get; set; }
        public string? Summary { get; set; }

        /// <summary>What [Tags] declared. The manifest wrote them all along.</summary>
        public List<string>? Tags { get; set; }
        public int SuccessStatusCode { get; set; }
        public bool IsVoid { get; set; }
        public ResponseDto? Response { get; set; }
        public string? Description { get; set; }
        public List<ParamDto>? Parameters { get; set; }
        public BodyDto? RequestBody { get; set; }
        public List<ErrorDto>? Errors { get; set; }

        /// <summary>Statuses the pipeline answers with that no declared error names.</summary>
        public List<int>? ProblemStatusCodes { get; set; }

        public AuthDto? Authorization { get; set; }
        public List<ExampleDto>? RequestExamples { get; set; }
        public List<ResponseExampleDto>? ResponseExamples { get; set; }
    }

    private sealed class ResponseDto { public string? Type { get; set; } }
    private sealed class ExampleDto { public string? Name { get; set; } public string? Summary { get; set; } public string? Json { get; set; } }
    private sealed class ResponseExampleDto { public int StatusCode { get; set; } public string? Name { get; set; } public string? Json { get; set; } }
    private sealed class ParamDto { public string? Name { get; set; } public string? In { get; set; } public string? Type { get; set; } public bool IsRequired { get; set; } public string? DefaultValue { get; set; } }
    private sealed class BodyDto
    {
        public List<PropDto>? Properties { get; set; }

        /// <summary>The body is the single property's type, not a record wrapping it.</summary>
        public bool Direct { get; set; }
    }
    private sealed class PropDto
    {
        public string? Name { get; set; }
        public string? WireName { get; set; }
        public string? Type { get; set; }
        public bool IsRequired { get; set; }
        public bool IsNullable { get; set; }
        public bool IsEnum { get; set; }

        // The constraints, under the JSON Schema keywords the manifest writes them with — see
        // WireConstraintsJson. Read here and copied onto the property schema by RenderConstraints.
        public int? MinLength { get; set; }
        public int? MaxLength { get; set; }
        public string? Pattern { get; set; }
        public string? Format { get; set; }
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
        public double? ExclusiveMinimum { get; set; }
        public double? ExclusiveMaximum { get; set; }
        public int? MinItems { get; set; }
        public int? MaxItems { get; set; }
    }
    private sealed class ErrorDto { public string? Code { get; set; } public int StatusCode { get; set; } }
    private sealed class AuthDto
    {
        public List<string>? RequiredPermissions { get; set; }

        /// <summary>The manifest already carried it; only this document was not reading it.</summary>
        public bool AllowAnonymous { get; set; }
    }
    private sealed class TypeDto
    {
        /// <summary>The fully-qualified type name — the identity used to tell homonyms apart.</summary>
        public string? Type { get; set; }

        public string? SimpleName { get; set; }
        public string? Kind { get; set; }
        public string? IdType { get; set; }
        public List<PropDto>? Properties { get; set; }

        /// <summary>
        ///     Enum member names. The manifest writes them under <c>"values"</c>, and case-insensitive
        ///     matching cannot bridge two different names: binding this to <c>enumValues</c> silently
        ///     left every enum schema empty. <c>"values"</c> is also what the two other manifest
        ///     consumers bind to, and module manifests are read back from already-compiled assemblies,
        ///     so the reader is the side that has to move.
        /// </summary>
        [JsonPropertyName("values")]
        public List<string>? EnumValues { get; set; }
    }
}
