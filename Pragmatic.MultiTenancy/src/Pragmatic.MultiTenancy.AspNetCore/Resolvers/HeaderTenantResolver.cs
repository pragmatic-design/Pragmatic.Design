using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Pragmatic.MultiTenancy.Resolvers;

/// <summary>
///     Resolves tenant from an HTTP header (default: <c>X-Tenant-Id</c>).
///     Header name is configurable via <see cref="MultiTenancyOptions.TenantHeaderName" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>Trust model.</b> This resolver trusts the header value as-is (length-capped only). A header
///         is client-supplied and therefore spoofable on its own, so it is safe only under one of these
///         deployment invariants — at least one MUST hold:
///     </para>
///     <list type="bullet">
///         <item>
///             <b>Gateway in front (recommended).</b> The Pragmatic Gateway strips any client
///             <c>X-Tenant-Id</c>, resolves the tenant from the signed JWT (or, opt-in, the subdomain),
///             and re-injects a gateway-authoritative header. The app must not be reachable bypassing it.
///         </item>
///         <item>
///             <b><see cref="MultiTenancyOptions.EnforceTenantClaim" /> (default <c>true</c>).</b> For any
///             <i>authenticated</i> request the resolved tenant must equal the user's tenant claim, or the
///             request is rejected — this closes header spoofing independently of the gateway.
///         </item>
///     </list>
///     <para>
///         An <i>anonymous</i> request that reaches the app directly (no gateway) with a forged header is
///         trusted only up to what downstream ownership / scoped-visibility filters permit. Do not disable
///         <c>EnforceTenantClaim</c> on a header-resolved deployment that is not behind the gateway.
///     </para>
/// </remarks>
public sealed class HeaderTenantResolver(
    IHttpContextAccessor accessor,
    IOptions<MultiTenancyOptions> options) : ITenantResolver
{
    // Maximum accepted header length — prevents memory-pressure from crafted headers.
    private const int MaxHeaderLength = 128;

    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var raw = accessor.HttpContext?.Request.Headers[options.Value.TenantHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
            return ValueTask.FromResult<string?>(null);

        var value = raw.Trim();
        if (value.Length > MaxHeaderLength)
            return ValueTask.FromResult<string?>(null);

        return ValueTask.FromResult<string?>(value);
    }
}
