namespace Pragmatic.Endpoints.Mcp;

/// <summary>
///     Options for exposing [McpTool] endpoints over the Model Context Protocol.
/// </summary>
public sealed class McpOptions
{
    /// <summary>Route where the MCP server is mapped. Default: "/mcp".</summary>
    public string Path { get; set; } = "/mcp";

    /// <summary>
    ///     Whether the MCP endpoint requires authorization. Default <c>true</c> — the MCP surface
    ///     is authenticated like any other endpoint. Set to <c>false</c> to expose it anonymously
    ///     (e.g. behind a trusted gateway); doing so lets any caller list and invoke every
    ///     <c>[McpTool]</c> endpoint that is itself anonymous.
    /// </summary>
    public bool RequireAuthorization { get; set; } = true;

    /// <summary>
    ///     Named authorization policy applied to the MCP endpoint when
    ///     <see cref="RequireAuthorization" /> is <c>true</c>. Null uses the default policy.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    ///     Headers forwarded from the incoming MCP HTTP request to the self-call that executes
    ///     the tool. Default is <c>Authorization</c> only, so identity travels as a verified token.
    /// </summary>
    /// <remarks>
    ///     Do NOT add client-asserted identity headers (e.g. <c>X-User-Id</c>, <c>X-User-Roles</c>,
    ///     <c>X-User-Permissions</c>, <c>X-Tenant-Id</c>) unless a trusted gateway sets them: they are
    ///     spoofable by the MCP client, and any downstream middleware that trusts them would grant
    ///     impersonation. If you forward tenant, match the downstream header name
    ///     (the Identity header middleware reads <c>X-User-Tenant</c>, not <c>X-Tenant-Id</c>).
    /// </remarks>
    public IList<string> ForwardedHeaders { get; } = ["Authorization"];

    /// <summary>
    ///     Base address for the self-call; null resolves the server's first bound address at startup.
    /// </summary>
    public Uri? SelfBaseAddress { get; set; }
}
