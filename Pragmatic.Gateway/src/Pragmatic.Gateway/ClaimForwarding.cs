using System.Net.Http.Headers;
using System.Security.Claims;

namespace Pragmatic.Gateway;

/// <summary>
///     Forwards configured JWT claims to the backend as <c>X-Claim-{type}</c> request headers. The gateway
///     is the trust boundary: each header is stripped of any client-supplied value first, then set to the
///     authoritative value from the validated principal — so a backend may trust <c>X-Claim-*</c> exactly
///     when it is only reachable through the gateway.
/// </summary>
public static class ClaimForwarding
{
    /// <summary>Header prefix under which each forwarded claim is written.</summary>
    public const string HeaderPrefix = "X-Claim-";

    /// <summary>
    ///     Applies the forwarding rule to the outgoing proxy request headers: for each claim type, remove
    ///     any inbound copy (anti-spoofing) and, if the principal carries that claim, set it authoritatively.
    ///     A claim absent from the principal leaves no header — so a spoofed inbound value cannot survive.
    /// </summary>
    public static void Apply(IReadOnlyList<string> claimTypes, ClaimsPrincipal user, HttpRequestHeaders headers)
    {
        foreach (var claimType in claimTypes)
        {
            var headerName = HeaderPrefix + claimType;
            headers.Remove(headerName);

            var value = user.FindFirst(claimType)?.Value;
            if (!string.IsNullOrEmpty(value))
                headers.TryAddWithoutValidation(headerName, value);
        }
    }
}
