namespace Pragmatic.Endpoints.Mcp;

/// <summary>
///     One [McpTool] endpoint resolved from the manifest: the MCP tool identity plus
///     everything needed to execute it over self-HTTP.
/// </summary>
public sealed record McpToolDescriptor(
    string Name,
    string Description,
    string InputSchemaJson,
    string HttpMethod,
    string FullRoute,
    IReadOnlyList<OpenApi.ManifestParam> Parameters,
    IReadOnlyList<OpenApi.ManifestProperty> BodyProperties,
    string? IdempotencyHeader);
