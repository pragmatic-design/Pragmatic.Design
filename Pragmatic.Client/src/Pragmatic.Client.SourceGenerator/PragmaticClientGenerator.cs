using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Pragmatic.Client.SourceGenerator;

/// <summary>
///     Source generator that reads API manifests and generates typed client code at compile time.
///     Supports two discovery modes:
///     - Mode A (AdditionalFiles): explicit manifest.json in the project
///     - Mode B (Assembly refs): reads [PragmaticMetadata] attributes from referenced assemblies (Private="false")
///     Mode A takes priority. Mode B enables zero-file-sync: reference the domain assembly, SG reads the manifest.
///     Produces: interface, HTTP client, DTOs, error types, DI registration.
///     Zero domain DLL dependency at runtime — client only needs Pragmatic.Result + Pragmatic.Client.
/// </summary>
[Generator]
public sealed partial class PragmaticClientGenerator : IIncrementalGenerator
{
    private const int ManifestMetadataCategory = 18;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Mode A: Find manifest files in AdditionalTexts
        var fileManifests = context.AdditionalTextsProvider
            .Where(static file =>
                file.Path.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                file.Path.EndsWith("PragmaticManifest.json", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, ct) => file.GetText(ct)?.ToString())
            .Where(static text => text is not null)
            .Collect();

        // Mode B: Read manifests from [PragmaticMetadata] assembly attributes on referenced assemblies
        var assemblyManifests = context.CompilationProvider
            .Select(static (compilation, _) => ExtractManifestsFromReferences(compilation));

        // Merge both discovery modes
        var allManifests = fileManifests.Combine(assemblyManifests);

        // Read boundary filter from build property
        var boundaryFilter = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
            {
                options.GlobalOptions.TryGetValue("build_property.PragmaticClientBoundaries", out var value);
                return value;
            });

        // Combine manifests + filter + assembly name (for namespace)
        var input = allManifests
            .Combine(boundaryFilter)
            .Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "Client"));

        context.RegisterSourceOutput(input, (ctx, data) =>
        {
            var (((files, assemblies), filter), assemblyName) = data;

            var boundaries = filter?.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var anyGenerated = false;

            // Per-run state: types shared by several boundaries must be emitted once (see AddSourceOnce).
            // Reset here so a previous run in the same process cannot suppress this run's output.
            _emittedSources = new Dictionary<string, string>(StringComparer.Ordinal);
            _generatedDtos = [];
            _pagedItemTypes = new HashSet<string>(StringComparer.Ordinal);
            _wireTypes = new HashSet<string>(StringComparer.Ordinal);

            // Mode A first — explicit files take priority over assembly discovery
            for (var i = 0; i < files.Length; i++)
            {
                if (files[i] is { } json)
                    anyGenerated |= TryGenerateFromManifest(ctx, json, assemblyName, boundaries, processed, "File" + i);
            }

            // Mode B — assembly-discovered manifests (skip already-processed assemblies)
            for (var i = 0; i < assemblies.Length; i++)
                anyGenerated |= TryGenerateFromManifest(ctx, assemblies[i], assemblyName, boundaries, processed, "Asm" + i);

            // PagedResult<T> is shared by every boundary client in the project namespace —
            // emitting it per boundary would collide (CS0101) on multi-boundary clients.
            if (anyGenerated)
            {
                GeneratePagedResult(ctx, assemblyName);

                // The JSON metadata, written longhand: a [JsonSerializable] context would need System.Text
                // .Json's own generator, which does not see this one's output. Without it the SDK is
                // reflection-only — the opposite of what a WebAssembly or MAUI consumer needs.
                var jsonModel = ClientJsonContextBuilder.Build(
                    assemblyName,
                    _generatedDtos ?? [],
                    _pagedItemTypes ?? new HashSet<string>(StringComparer.Ordinal),
                    _wireTypes ?? new HashSet<string>(StringComparer.Ordinal));

                Emit(ctx, new global::Pragmatic.SourceGen.PragmaticJsonContextTemplate(
                    jsonModel, "PragmaticJsonContext.g.cs"));
            }

            // Everything above only staged its output; this is what writes it. Deferred so that a type
            // reachable from several manifests is emitted once, in its most complete form.
            FlushSources(ctx);
        });
    }

    /// <summary>
    ///     Scans referenced assemblies for [PragmaticMetadata(category=18)] attributes
    ///     and extracts the manifest JSON. This enables Mode B: compile-only references
    ///     with Private="false" provide manifests without file sync.
    /// </summary>
    private static ImmutableArray<string> ExtractManifestsFromReferences(Compilation compilation)
    {
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            foreach (var attr in assembly.GetAttributes())
            {
                if (attr.AttributeClass is null || attr.AttributeClass.Name != "PragmaticMetadataAttribute")
                    continue;
                if (attr.ConstructorArguments.Length < 3)
                    continue;

                // First arg = MetadataCategory enum (stored as underlying int)
                var categoryArg = attr.ConstructorArguments[0];
                if (!(categoryArg.Value is int category) || category != ManifestMetadataCategory)
                    continue;

                // Third arg = JSON string
                var jsonArg = attr.ConstructorArguments[2];
                if (jsonArg.Value is string json && json.Length > 0)
                    builder.Add(json);
            }
        }

        return builder.Count > 0 ? builder.ToImmutable() : ImmutableArray<string>.Empty;
    }

    /// <summary>
    ///     Reports endpoints that declare a result the manifest does not type. Separate from PRAG2301: there a
    ///     type is named and merely undescribed, here the manifest says nothing at all, and the two are fixed
    ///     in different places.
    /// </summary>
    private static void ReportMissingResponseTypes(SourceProductionContext ctx, List<EndpointDto> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            if (!endpoint.IsVoid && endpoint.Response?.Type is null)
                ctx.ReportDiagnostic(Diagnostic.Create(
                    ClientDiagnostics.ResponseTypeMissing, Location.None,
                    endpoint.OperationId ?? "(unnamed)"));
        }
    }

    /// <summary>
    ///     Reports every type the manifest did not describe, then clears the set so the next manifest starts
    ///     clean (the collector is thread-static and outlives a single generation pass).
    /// </summary>
    private static void ReportUnresolvedTypes(SourceProductionContext ctx)
    {
        if (_unresolvedTypes is not { Count: > 0 }) return;

        foreach (var typeName in _unresolvedTypes.OrderBy(t => t, StringComparer.Ordinal))
            ctx.ReportDiagnostic(Diagnostic.Create(ClientDiagnostics.UnresolvedType, Location.None, typeName));

        _unresolvedTypes.Clear();
    }

    /// <summary>
    ///     Lightweight JSON peek to extract assembly name for deduplication.
    /// </summary>
    private static string? ExtractAssemblyNameFromJson(string json)
    {
        try
        {
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("assembly", out var prop))
                    return prop.GetString();
            }
        }
        catch (JsonException)
        {
            // Malformed manifest: it has no usable assembly name to deduplicate on. Not reported here —
            // GenerateFromManifest hits the same JSON immediately after and raises PRAG2300 with the
            // parser's message, which is the more useful place to say it.
            return null;
        }

        return null;
    }

    private static bool TryGenerateFromManifest(
        SourceProductionContext ctx, string json, string assemblyName,
        string[]? boundaries, HashSet<string> processed, string errorPrefix)
    {
        var asmName = ExtractAssemblyNameFromJson(json);
        if (asmName is not null && !processed.Add(asmName)) return false;

        try
        {
            return GenerateFromManifest(ctx, json, assemblyName, boundaries);
        }
        catch (Exception ex)
        {
            // Reported as a diagnostic, not left as a comment in a generated file: a comment keeps the build
            // green while every type the consumer expects is missing, far from where the failure happens.
            ctx.ReportDiagnostic(Diagnostic.Create(
                ClientDiagnostics.ManifestUnreadable, Location.None, asmName ?? errorPrefix, ex.Message));
            return false;
        }
    }

    private static bool GenerateFromManifest(
        SourceProductionContext ctx, string json, string clientNamespace, string[]? boundaryFilter)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var manifest = JsonSerializer.Deserialize<ManifestDto>(json, options);
        if (manifest?.Endpoints is null || manifest.Endpoints.Count == 0) return false;

        // Filter endpoints by boundary if specified
        var endpoints = manifest.Endpoints;
        if (boundaryFilter is { Length: > 0 })
        {
            endpoints = endpoints
                .Where(e => e.OperationId is not null &&
                    boundaryFilter.Any(b => e.OperationId.StartsWith(b, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (endpoints.Count == 0)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    ClientDiagnostics.BoundaryFilterMatchedNothing, Location.None,
                    string.Join(";", boundaryFilter), manifest.Assembly ?? clientNamespace));
                return false;
            }
        }

        if (endpoints.Count == 0) return false;

        var boundaryName = DeriveBoundaryName(manifest.Assembly ?? clientNamespace);
        var ns = clientNamespace;

        // Build known entity/DTO type names for response type resolution
        var entityTypes = new HashSet<string>(StringComparer.Ordinal);
        if (manifest.Types is not null)
            foreach (var t in manifest.Types.Where(t => (t.Kind == "entity" || t.Kind == "dto") && t.SimpleName is not null))
                entityTypes.Add(t.SimpleName!);
        _knownEntityTypes = entityTypes;

        // Enums are emitted by GenerateEnums, so they can be referenced by name in signatures.
        var enumTypes = new HashSet<string>(StringComparer.Ordinal);
        if (manifest.Types is not null)
            foreach (var t in manifest.Types.Where(t => t.Kind == "enum" && t.SimpleName is not null))
                enumTypes.Add(t.SimpleName!);
        _knownEnumTypes = enumTypes;

        // Collect error codes + extensions for error mapping in HttpClient
        var errorTypes = new List<ErrorInfo>();
        if (manifest.Types is not null)
            foreach (var t in manifest.Types.Where(t => t.Kind == "error" && t.SimpleName is not null))
                errorTypes.Add(new ErrorInfo(t.SimpleName!, t.ErrorCode ?? "UNKNOWN", t.ErrorStatusCode ?? 500, t.Extensions));

        // Generate interface
        GenerateInterface(ctx, ns, boundaryName, endpoints);

        // Generate HTTP client with error mapping
        GenerateHttpClient(ctx, ns, boundaryName, endpoints, errorTypes);

        // Generate request DTOs for endpoints with body
        GenerateRequestDtos(ctx, ns, endpoints);

        // Generate response DTOs (entity types from manifest)
        GenerateResponseDtos(ctx, ns, manifest.Types);

        // Generate enums
        GenerateEnums(ctx, ns, manifest.Types);

        // Generate error types
        GenerateErrors(ctx, ns, manifest.Types);

        // Generate DI registration
        GenerateRegistration(ctx, ns, boundaryName);

        ReportMissingResponseTypes(ctx, endpoints);
        ReportUnresolvedTypes(ctx);
        return true;
    }


    // Internal helper for error mapping code generation
    internal sealed class ErrorInfo(string typeName, string code, int statusCode, List<ErrorExtDto>? extensions)
    {
        public string TypeName { get; } = typeName;
        public string Code { get; } = code;
        public int StatusCode { get; } = statusCode;
        public List<ErrorExtDto> Extensions { get; } = extensions ?? new List<ErrorExtDto>();
    }

    internal sealed class ErrorExtDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
    }

    // DTOs for JSON deserialization
    private sealed class ManifestDto
    {
        public string? Assembly { get; set; }
        public List<EndpointDto>? Endpoints { get; set; }
        public List<TypeDto>? Types { get; set; }
    }

    internal sealed class EndpointDto
    {
        public string? OperationId { get; set; }
        public string? HttpMethod { get; set; }
        public string? FullRoute { get; set; }
        public string? Summary { get; set; }
        public bool IsVoid { get; set; }
        public ResponseDto? Response { get; set; }
        public List<ParamDto>? Parameters { get; set; }
        public BodyDto? RequestBody { get; set; }
    }

    internal sealed class ResponseDto { public string? Type { get; set; } }
    internal sealed class ParamDto { public string? Name { get; set; } public string? In { get; set; } public string? Type { get; set; } }
    internal sealed class BodyDto { public List<PropDto>? Properties { get; set; } }
    internal sealed class PropDto { public string? Name { get; set; } public string? Type { get; set; } public bool IsRequired { get; set; } public bool IsNullable { get; set; } public bool IsEnum { get; set; } }
    internal sealed class TypeDto
    {
        public string? SimpleName { get; set; }
        public string? Kind { get; set; }
        public string? ErrorCode { get; set; }
        public int? ErrorStatusCode { get; set; }
        public List<PropDto>? Properties { get; set; }
        public List<ErrorExtDto>? Extensions { get; set; }
        // Manifest JSON uses "values" key for enum members
        [System.Text.Json.Serialization.JsonPropertyName("values")]
        public List<string>? EnumValues { get; set; }
    }
}
