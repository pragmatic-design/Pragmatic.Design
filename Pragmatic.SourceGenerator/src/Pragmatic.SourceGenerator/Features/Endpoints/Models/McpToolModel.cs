namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     [McpTool] configuration — exposes the endpoint as an MCP tool through the manifest.
/// </summary>
internal sealed record McpToolModel
{
    /// <summary>Tool name override; null → sanitized operation id.</summary>
    public string? Name { get; init; }

    /// <summary>Tool description; null → endpoint summary.</summary>
    public string? Description { get; init; }
}
