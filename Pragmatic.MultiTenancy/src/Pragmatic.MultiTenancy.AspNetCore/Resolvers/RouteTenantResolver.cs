using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Pragmatic.MultiTenancy.Resolvers;

/// <summary>
///     Resolves tenant from a route parameter (default: <c>tenantId</c>).
///     Route parameter name is configurable via <see cref="MultiTenancyOptions.TenantRouteParameter" />.
/// </summary>
public sealed class RouteTenantResolver(
    IHttpContextAccessor accessor,
    IOptions<MultiTenancyOptions> options) : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var value = accessor.HttpContext?.Request.RouteValues[options.Value.TenantRouteParameter]?.ToString();
        return ValueTask.FromResult(value);
    }
}
