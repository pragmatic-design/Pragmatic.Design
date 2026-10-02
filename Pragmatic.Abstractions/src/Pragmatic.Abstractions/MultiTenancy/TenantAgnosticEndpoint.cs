namespace Pragmatic.MultiTenancy;

/// <summary>
///     Endpoint metadata saying this route is not about a tenant, so tenant resolution must not
///     refuse it when no tenant is supplied.
/// </summary>
/// <remarks>
///     <para>
///         With <c>RequireTenant</c> — the default — every request without a resolvable tenant is
///         short-circuited with 400 unless its endpoint carries this marker. That is right for the
///         domain surface and wrong for everything underneath it: a liveness probe carries no tenant
///         header, and the published contract is what you fetch <em>before</em> you are anyone.
///         Without the marker <c>/health</c> answers <b>400</b> without the header and <b>200</b>
///         with it, so a Kubernetes probe would mark the application dead and restart it forever.
///     </para>
///     <para>
///         <b>Metadata rather than a list of paths</b>, and that is not a style preference. Both
///         routes involved are configurable — the health path through <c>HostHealthOptions</c>, the
///         document's through <c>MapPragmaticOpenApi(pattern)</c> — so a default list of strings
///         would be right only for applications that changed nothing, and silently wrong for the
///         rest. A marker travels with the endpoint wherever it is mapped.
///     </para>
///     <para>
///         It lives in Abstractions so that a package can mark its endpoint without referencing
///         multi-tenancy: the OpenAPI package should not learn about tenants to say that a static
///         document has none.
///     </para>
///     <para>
///         ⚠️ It relaxes the <em>requirement</em>, not the resolution. A tenant supplied on one of
///         these routes is still resolved, still checked against the caller's claim, and still
///         published downstream — the only thing that changes is that its absence is not an error.
///     </para>
///     <example>
///         <code>
/// endpoints.MapGet("/health", Handler)
///     .WithMetadata(TenantAgnosticEndpoint.Instance);
/// </code>
///     </example>
/// </remarks>
public sealed class TenantAgnosticEndpoint
{
    /// <summary>The single instance to attach; the type carries the meaning, not any state.</summary>
    public static readonly TenantAgnosticEndpoint Instance = new();

    private TenantAgnosticEndpoint()
    {
    }
}
