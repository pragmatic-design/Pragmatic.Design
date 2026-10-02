namespace Pragmatic.Endpoints.Mcp;

/// <summary>
///     Holds the base address for tool self-calls, resolved at pipeline-build time
///     (options override → first bound server address → localhost fallback).
/// </summary>
public sealed class McpSelfAddressHolder
{
    /// <summary>The self-call base address.</summary>
    public Uri Address { get; set; } = new("http://localhost");
}
