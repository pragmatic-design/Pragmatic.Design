using System.Text.Json;

namespace Pragmatic.Client.Cli;

/// <summary>
///     Minimal manifest reader for the CLI: deserializes PragmaticManifest.json
///     (per-module or host-aggregated with "modules": [...]) into flat module documents.
/// </summary>
public static class ManifestJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<ManifestDoc> Read(string json)
    {
        var doc = JsonSerializer.Deserialize<ManifestDoc>(json, Options)
                  ?? throw new InvalidOperationException("Manifest could not be parsed.");

        if (doc.Modules is { Count: > 0 } modules)
            return modules;

        return [doc];
    }

    public sealed class ManifestDoc
    {
        public string? Assembly { get; set; }
        public List<EndpointDoc>? Endpoints { get; set; }
        public List<TypeDoc>? Types { get; set; }
        public List<ManifestDoc>? Modules { get; set; }

        public string BoundaryName
        {
            get
            {
                var assembly = Assembly ?? "Api";
                var lastDot = assembly.LastIndexOf('.');
                return lastDot >= 0 ? assembly[(lastDot + 1)..] : assembly;
            }
        }
    }

    public sealed class EndpointDoc
    {
        public string? OperationId { get; set; }
        public string? HttpMethod { get; set; }
        public string? FullRoute { get; set; }
        public string? Summary { get; set; }
        public bool IsVoid { get; set; }
        public ResponseDoc? Response { get; set; }
        public List<ParamDoc>? Parameters { get; set; }
        public BodyDoc? RequestBody { get; set; }
        public List<ErrorDoc>? Errors { get; set; }
        public string? IdempotencyHeader { get; set; }
    }

    public sealed class ResponseDoc
    {
        public string? Type { get; set; }
        public bool IsPaged { get; set; }
        public bool IsStream { get; set; }
    }

    public sealed class ParamDoc
    {
        public string? Name { get; set; }
        public string? In { get; set; }
        public string? Type { get; set; }
        public bool IsRequired { get; set; }
    }

    public sealed class BodyDoc
    {
        public List<PropDoc>? Properties { get; set; }
    }

    public sealed class PropDoc
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public bool IsRequired { get; set; }
        public bool IsNullable { get; set; }
        public bool IsEnum { get; set; }
    }

    public sealed class ErrorDoc
    {
        public string? Code { get; set; }
        public int StatusCode { get; set; }
    }

    public sealed class TypeDoc
    {
        public string? SimpleName { get; set; }
        public string? Kind { get; set; }
        public string? ErrorCode { get; set; }
        public int? ErrorStatusCode { get; set; }
        public List<PropDoc>? Properties { get; set; }
        public List<string>? Values { get; set; }
    }
}
