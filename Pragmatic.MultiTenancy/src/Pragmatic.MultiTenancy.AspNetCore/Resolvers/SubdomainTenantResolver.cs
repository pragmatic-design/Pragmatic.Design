using System.Net;
using Microsoft.AspNetCore.Http;

namespace Pragmatic.MultiTenancy.Resolvers;

/// <summary>
///     Resolves tenant from the request's subdomain.
///     For <c>acme.app.com</c>, returns <c>acme</c>.
///     Requires at least 3 domain segments (subdomain.domain.tld).
///     IP address hosts (e.g. <c>192.168.1.1</c>) are rejected and return <c>null</c>.
/// </summary>
public sealed class SubdomainTenantResolver(IHttpContextAccessor accessor) : ITenantResolver
{
    // Maximum accepted subdomain length.
    private const int MaxSubdomainLength = 63;

    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var host = accessor.HttpContext?.Request.Host.Host;
        if (string.IsNullOrEmpty(host))
            return ValueTask.FromResult<string?>(null);

        // Reject bare IP addresses — splitting "192.168.1.1" would return "192" as tenant.
        if (IPAddress.TryParse(host, out _))
            return ValueTask.FromResult<string?>(null);

        var parts = host.Split('.');
        // Need at least subdomain.domain.tld (3 parts)
        if (parts.Length < 3)
            return ValueTask.FromResult<string?>(null);

        var subdomain = parts[0];
        if (subdomain.Length > MaxSubdomainLength)
            return ValueTask.FromResult<string?>(null);

        return ValueTask.FromResult<string?>(subdomain);
    }
}
