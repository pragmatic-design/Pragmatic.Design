using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Manifest.Models;

namespace Pragmatic.SourceGenerator.Features.Manifest.Templates;

/// <summary>
///     Generates _Metadata.Manifest.g.cs containing the manifest JSON as a static class.
///     The JSON is built via MetadataJsonBuilder and emitted as a const string field.
/// </summary>
internal sealed class ManifestJsonTemplate : CSharpTemplate
{
    private readonly ManifestModel _model;

    public ManifestJsonTemplate(ManifestModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Manifest";
    protected override string? SourceInfo => $"API Manifest for {_model.Assembly}";

    public override Artifact RenderOutput()
        => new("_Metadata.PragmaticManifest.g.cs", ToSourceText());

    /// <summary>
    ///     The compact manifest document this template puts inside <c>[assembly: PragmaticMetadata]</c>.
    /// </summary>
    /// <remarks>
    ///     Exposed so a host that declares its own module can hand the host aggregation the very same
    ///     string, rather than a second rendering of it: the attribute is written by this generator run
    ///     and cannot be read back off the compilation.
    /// </remarks>
    public string BuildCompactJson() => BuildManifestJson(indent: false);

    /// <summary>Whether this model has anything worth writing. Mirror it wherever the output is gated.</summary>
    public bool HasContent => _model.Endpoints.Length > 0 || _model.Types.Length > 0;

    protected override bool Validate() => HasContent;

    public override void RenderFile()
    {
        var json = BuildManifestJson(indent: true);

        // Build compact JSON for assembly attribute (Host MetadataReader reads this)
        var compactJson = BuildManifestJson(indent: false);

        Comment($"Aggregated API manifest — {_model.Endpoints.Length} endpoints, {_model.Types.Length} types, {_model.Permissions.Length} permissions");

        // Assembly attribute for Host aggregation via MetadataReader
        AppendLine($"[assembly: global::Pragmatic.Composition.Attributes.PragmaticMetadataAttribute((global::Pragmatic.Composition.Metadata.MetadataCategory)18, \"1.0.0\", \"\"\"{compactJson}\"\"\")]");
        AppendLine();

        AppendLine($"namespace {_model.Assembly.Replace("-", "_")};");
        AppendLine();

        AppendLine("/// <summary>");
        AppendLine("///     Compile-time API manifest for OpenAPI enrichment, client generation, and tooling.");
        AppendLine("/// </summary>");
        AppendLine("internal static class PragmaticManifest");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("/// <summary>Raw JSON manifest.</summary>");
        AppendLine("internal const string Json = \"\"\"");
        // Emit pre-built JSON as raw string literal
        foreach (var line in json.Split('\n'))
            AppendLine(line.TrimEnd('\r'));
        AppendLine("\"\"\";");

        if (_model.RegistersAtLoad)
            RenderRegistration(compactJson);

        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     Registers the manifest at module load, so <c>AddPragmaticOpenApi()</c> has something to read in
    ///     a host without Composition, not only in a Composition host's aggregated manifest.
    /// </summary>
    /// <remarks>
    ///     It registers the compact text, not <c>Json</c>: that is the string the assembly attribute
    ///     carries and a Composition host embeds verbatim, so a process holding both registrations reads
    ///     the module once (<c>ManifestReader.Parse</c> matches on the text).
    /// </remarks>
    private void RenderRegistration(string compactJson)
    {
        AppendLine();
        AppendLine("/// <summary>The manifest as the assembly attribute carries it, and as a Composition host embeds it.</summary>");
        AppendLine($"internal const string CompactJson = \"\"\"{compactJson}\"\"\";");
        AppendLine();
        AppendLine("/// <summary>Registers the manifest at module load time, for the runtime OpenAPI enrichment.</summary>");
        AppendLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        AppendLine("internal static void RegisterManifest()");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("global::Pragmatic.Endpoints.Manifest.ManifestRegistry.Register(CompactJson);");
        DecreaseIndent();
        AppendLine("}");
    }

    private string BuildManifestJson(bool indent = true)
    {
        var b = new MetadataJsonBuilder(indent);
        b.StartObject();

        b.Property("$schema").Value("pragmatic-manifest/v1");
        b.Property("version").Value(_model.SchemaVersion);
        b.Property("assembly").Value(_model.Assembly);

        // Boundaries
        b.Property("boundaries").StartArray();
        foreach (var boundary in _model.Boundaries)
        {
            b.StartObject();
            b.Property("name").Value(boundary.Name);
            b.Property("type").Value(boundary.Type);
            b.EndObject();
        }
        b.EndArray();

        // Endpoints
        b.Property("endpoints").StartArray();
        foreach (var ep in _model.Endpoints)
            RenderEndpoint(b, ep);
        b.EndArray();

        // Types
        b.Property("types").StartArray();
        foreach (var type in _model.Types)
            RenderType(b, type);
        b.EndArray();

        // Actions
        b.Property("actions").StartArray();
        foreach (var action in _model.Actions)
        {
            b.StartObject();
            b.Property("type").Value(action.Type);
            b.Property("simpleName").Value(action.SimpleName);
            b.Property("kind").Value(action.Kind);
            if (action.ReturnType is not null) b.Property("returnType").Value(action.ReturnType);
            if (action.EntityType is not null) b.Property("entityType").Value(action.EntityType);
            if (action.Boundary is not null) b.Property("boundary").Value(action.Boundary);
            b.Property("isVoid").Value(action.IsVoid);
            b.EndObject();
        }
        b.EndArray();

        // Permissions
        b.Property("permissions").StartArray();
        foreach (var perm in _model.Permissions)
        {
            b.StartObject();
            b.Property("name").Value(perm.Name);
            // "endpoint" stays implicit: it is what every permission was until auto-derivation existed,
            // and writing it would rewrite the manifest of every assembly that never turns the switch on.
            if (perm.Source is not null && perm.Source != "endpoint")
                b.Property("source").Value(perm.Source);
            b.EndObject();
        }
        b.EndArray();

        b.EndObject();
        return b.ToString();
    }

    private static void RenderEndpoint(MetadataJsonBuilder b, ManifestEndpointModel ep)
    {
        b.StartObject();
        b.Property("operationId").Value(ep.OperationId);
        b.Property("httpMethod").Value(ep.HttpMethod);
        b.Property("fullRoute").Value(ep.FullRoute);
        if (ep.Summary is not null) b.Property("summary").Value(ep.Summary);
        if (ep.Description is not null) b.Property("description").Value(ep.Description);
        b.Property("successStatusCode").Value(ep.SuccessStatusCode);
        b.Property("isVoid").Value(ep.IsVoid);

        // Deprecation, rate limit, cache, tags, and file-upload metadata for OpenAPI.
        if (ep.IsDeprecated) b.Property("isDeprecated").Value(true);
        if (ep.RateLimitPolicy is not null) b.Property("rateLimitPolicy").Value(ep.RateLimitPolicy);
        if (ep.CacheDurationSeconds is { } cacheSeconds) b.Property("cacheDurationSeconds").Value(cacheSeconds);
        if (ep.MaxBodySizeBytes is { } maxBodyBytes) b.Property("maxBodySizeBytes").Value(maxBodyBytes);
        if (ep.IdempotencyHeader is not null) b.Property("idempotencyHeader").Value(ep.IdempotencyHeader);
        if (ep.McpTool is { } mcp)
        {
            b.Property("mcp").StartObject();
            b.Property("enabled").Value(true);
            if (mcp.Name is not null) b.Property("name").Value(mcp.Name);
            if (mcp.Description is not null) b.Property("description").Value(mcp.Description);
            b.EndObject();
        }

        if (ep.Tags.Length > 0)
        {
            b.Property("tags").StartArray();
            foreach (var tag in ep.Tags) b.Value(tag);
            b.EndArray();
        }

        if (ep.FileUpload is not null)
        {
            b.Property("fileUpload").StartObject();
            if (ep.FileUpload.MaxFileSizeBytes is { } maxBytes)
                b.Property("maxFileSizeBytes").Value(maxBytes);
            if (ep.FileUpload.AllowedContentTypes.Length > 0)
            {
                b.Property("allowedContentTypes").StartArray();
                foreach (var ct in ep.FileUpload.AllowedContentTypes) b.Value(ct);
                b.EndArray();
            }
            b.EndObject();
        }

        if (ep.ResponseType is not null)
        {
            b.Property("response").StartObject();
            b.Property("type").Value(ep.ResponseType.Type);
            if (ep.ResponseType.IsPaged) b.Property("isPaged").Value(true);
            if (ep.ResponseType.IsStream) b.Property("isStream").Value(true);
            b.EndObject();
        }

        // Parameters
        if (ep.Parameters.Length > 0)
        {
            b.Property("parameters").StartArray();
            foreach (var p in ep.Parameters)
            {
                b.StartObject();
                b.Property("name").Value(p.Name);
                b.Property("in").Value(p.In);
                b.Property("type").Value(p.Type);
                b.Property("isRequired").Value(p.IsRequired);
                if (p.DefaultValue is not null) b.Property("defaultValue").Value(p.DefaultValue);
                b.EndObject();
            }
            b.EndArray();
        }

        // Request body
        if (ep.RequestBody is not null && ep.RequestBody.Properties.Length > 0)
        {
            b.Property("requestBody").StartObject();
            // The body is the property's own type: the reader $refs it instead of building a wrapper.
            if (ep.RequestBody.IsDirect) b.Property("direct").Value(true);
            b.Property("properties").StartArray();
            foreach (var prop in ep.RequestBody.Properties)
            {
                b.StartObject();
                b.Property("name").Value(prop.Name);
                // Only when it differs: the manifest is read by the OpenAPI generator, which otherwise
                // camel-cases the name, and writing the same thing twice invites the two to disagree.
                if (prop.WireName is { Length: > 0 } wire) b.Property("wireName").Value(wire);
                b.Property("type").Value(prop.Type);
                b.Property("isRequired").Value(prop.IsRequired);
                b.Property("isNullable").Value(prop.IsNullable);
                if (prop.IsEnum) b.Property("isEnum").Value(true);
                // The OpenAPI generator read maxLength for request bodies from the start, and nothing
                // wrote it; then it was written alone. The whole set goes now, under the schema's names.
                WireConstraintsJson.Write(b, prop.Constraints);
                b.EndObject();
            }
            b.EndArray();
            b.EndObject();
        }

        // Errors
        if (ep.Errors.Length > 0)
        {
            b.Property("errors").StartArray();
            foreach (var e in ep.Errors)
            {
                b.StartObject();
                b.Property("type").Value(e.Type);
                b.Property("code").Value(e.Code);
                b.Property("statusCode").Value(e.StatusCode);
                if (e.Extensions.Length > 0)
                {
                    b.Property("extensions").StartArray();
                    foreach (var ext in e.Extensions)
                    {
                        b.StartObject();
                        b.Property("name").Value(ext.Name);
                        b.Property("type").Value(ext.Type);
                        b.EndObject();
                    }
                    b.EndArray();
                }
                b.EndObject();
            }
            b.EndArray();
        }

        // What the pipeline answers with beyond the declared errors — the shipped document had none.
        if (ep.ProblemStatusCodes.Length > 0)
        {
            b.Property("problemStatusCodes").StartArray();
            foreach (var code in ep.ProblemStatusCodes)
                b.Value(code);
            b.EndArray();
        }

        // OpenAPI examples — payload emitted as a string, parsed at runtime
        if (ep.RequestExamples.Length > 0)
        {
            b.Property("requestExamples").StartArray();
            foreach (var example in ep.RequestExamples)
            {
                b.StartObject();
                if (example.Name is not null) b.Property("name").Value(example.Name);
                if (example.Summary is not null) b.Property("summary").Value(example.Summary);
                b.Property("json").Value(example.Json);
                b.EndObject();
            }
            b.EndArray();
        }

        if (ep.ResponseExamples.Length > 0)
        {
            b.Property("responseExamples").StartArray();
            foreach (var example in ep.ResponseExamples)
            {
                b.StartObject();
                b.Property("statusCode").Value(example.StatusCode);
                if (example.Name is not null) b.Property("name").Value(example.Name);
                b.Property("json").Value(example.Json);
                b.EndObject();
            }
            b.EndArray();
        }

        // Authorization
        if (ep.Authorization is not null)
        {
            b.Property("authorization").StartObject();
            b.Property("allowAnonymous").Value(ep.Authorization.AllowAnonymous);
            if (ep.Authorization.RequiredPermissions.Length > 0)
            {
                b.Property("requiredPermissions").StartArray();
                foreach (var p in ep.Authorization.RequiredPermissions) b.Value(p);
                b.EndArray();
            }
            b.EndObject();
        }

        b.EndObject();
    }

    private static void RenderType(MetadataJsonBuilder b, ManifestTypeModel type)
    {
        b.StartObject();
        b.Property("type").Value(type.Type);
        b.Property("simpleName").Value(type.SimpleName);
        b.Property("kind").Value(type.Kind.ToString().ToLowerInvariant());
        if (type.Boundary is not null) b.Property("boundary").Value(type.Boundary);
        if (type.IdType is not null) b.Property("idType").Value(type.IdType);
        if (type.ErrorCode is not null) b.Property("errorCode").Value(type.ErrorCode);
        if (type.ErrorStatusCode.HasValue) b.Property("errorStatusCode").Value(type.ErrorStatusCode.Value);

        if (type.Properties.Length > 0)
        {
            b.Property("properties").StartArray();
            foreach (var p in type.Properties)
            {
                b.StartObject();
                b.Property("name").Value(p.Name);
                if (p.WireName is { Length: > 0 } typeWire) b.Property("wireName").Value(typeWire);
                b.Property("type").Value(p.Type);
                b.Property("isRequired").Value(p.IsRequired);
                b.Property("isNullable").Value(p.IsNullable);
                if (p.IsEnum) b.Property("isEnum").Value(true);
                WireConstraintsJson.Write(b, p.Constraints);
                b.EndObject();
            }
            b.EndArray();
        }

        if (type.EnumValues.Length > 0)
        {
            b.Property("values").StartArray();
            foreach (var v in type.EnumValues) b.Value(v);
            b.EndArray();
        }

        b.EndObject();
    }
}
