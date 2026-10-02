namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Exposes this endpoint as an MCP (Model Context Protocol) tool when the host enables
///     <c>UseMcp()</c> — the package <c>Pragmatic.Endpoints.Mcp</c>, whose
///     <c>AddPragmaticMcp()</c> is the same registration for a host that has only an
///     <c>IServiceCollection</c>. The tool's input schema is derived from the endpoint's compile-time
///     manifest (route/query parameters + request body) — zero reflection.
/// </summary>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Post, "/api/reservations")]
/// [McpTool(Description = "Creates a reservation for a guest.")]
/// public partial class CreateReservationAction : DomainAction&lt;Guid&gt; { ... }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class McpToolAttribute : Attribute
{
    /// <summary>Tool name override; default is the sanitized operation id (boundary_operation).</summary>
    public string? Name { get; set; }

    /// <summary>Tool description shown to models; defaults to the endpoint summary.</summary>
    public string? Description { get; set; }
}
