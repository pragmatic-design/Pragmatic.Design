using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace Pragmatic.Composition.Remote;

/// <summary>
///     Propagates the caller's identity across a remote boundary call. Copies the inbound request's
///     <c>Authorization</c> header onto the outbound <c>/_pragmatic/invoke</c> request so the remote
///     host authenticates the originating principal.
/// </summary>
/// <remarks>
///     Registered as a message handler on each named remote <c>HttpClient</c>. Together with the
///     fail-closed invoke endpoint (<see cref="RemoteInvokeEndpointAuth"/>) this closes the trust
///     boundary: a call with no inbound credential reaches the remote host unauthenticated and is
///     rejected unless the deployment opts into <see cref="PragmaticRemoteInvokeOptions.AllowAnonymous"/>.
/// </remarks>
public sealed class PragmaticRemoteAuthHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PragmaticRemoteAuthHandler(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor
            ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        PropagateAuthorization(request);
        return base.SendAsync(request, cancellationToken);
    }

    private void PropagateAuthorization(HttpRequestMessage request)
    {
        // An explicit outbound credential (e.g. a configured service token) always wins.
        if (request.Headers.Authorization is not null)
            return;

        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return;

        var inbound = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(inbound)
            && AuthenticationHeaderValue.TryParse(inbound, out var parsed))
        {
            request.Headers.Authorization = parsed;
        }
    }
}
