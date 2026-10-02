using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Pragmatic.MultiTenancy.Resolvers;

/// <summary>
///     Resolves tenant from a JWT claim (default: <c>tenant_id</c>).
///     Claim type is configurable via <see cref="MultiTenancyOptions.TenantClaimType" />.
/// </summary>
public sealed class ClaimTenantResolver(
    IHttpContextAccessor accessor,
    IOptions<MultiTenancyOptions> options) : ITenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var value = accessor.HttpContext?.User.FindFirst(options.Value.TenantClaimType)?.Value;
        return ValueTask.FromResult(value);
    }
}
