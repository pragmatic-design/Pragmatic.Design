namespace Pragmatic.MultiTenancy;

/// <summary>
///     Resolves the current tenant identifier from ambient context.
///     Implementations may extract it from HTTP headers, subdomains, claims, or other sources.
/// </summary>
/// <remarks>
///     <para>
///     This is a transport-agnostic contract. It does NOT depend on ASP.NET Core.
///     HTTP-specific resolution strategies should implement this interface using
///     <c>IHttpContextAccessor</c> internally.
///     </para>
///     <para>
///     Resolution strategies include:
///     <list type="bullet">
///         <item><description>HTTP header (e.g., <c>X-Tenant-Id</c>)</description></item>
///         <item><description>Subdomain (e.g., <c>acme.example.com</c>)</description></item>
///         <item><description>JWT claim (e.g., <c>tenant_id</c>)</description></item>
///         <item><description>API key lookup</description></item>
///         <item><description>Route parameter</description></item>
///     </list>
///     </para>
/// </remarks>
public interface ITenantResolver
{
    /// <summary>
    ///     Attempts to resolve the current tenant identifier.
    /// </summary>
    /// <returns>The tenant ID if resolved; <c>null</c> otherwise.</returns>
    ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default);
}
