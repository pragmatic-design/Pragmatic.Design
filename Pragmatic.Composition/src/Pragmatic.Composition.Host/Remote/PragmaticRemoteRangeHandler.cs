using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Pragmatic.Composition.Remote;

/// <summary>
///     Propagates the end user's <c>Range</c> and <c>If-Range</c> onto the outbound
///     <c>/_pragmatic/invoke</c> call, so a remote boundary reads only the bytes that were asked for.
/// </summary>
/// <remarks>
///     <para>
///         Without this, the request the remote host sees is the host's own RPC <c>POST</c> — which
///         carries no range at all. The boundary would read the whole object out of storage and stream it
///         across the network so the calling host could throw away all but a slice of it: a seek into a
///         500 MB file would move 500 MB. Propagating the range end to end is the difference between the
///         two.
///     </para>
///     <para>
///         Registered as a message handler on each named remote <c>HttpClient</c>, alongside
///         <see cref="PragmaticRemoteAuthHandler" />. Harmless on non-file actions: a JSON envelope is
///         built by <c>Results.Ok</c>, which does not do range processing, so the header is ignored.
///     </para>
/// </remarks>
public sealed class PragmaticRemoteRangeHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates the handler.</summary>
    /// <param name="httpContextAccessor">Accessor for the inbound request.</param>
    public PragmaticRemoteRangeHandler(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor
            ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        PropagateRange(request);
        return base.SendAsync(request, cancellationToken);
    }

    private void PropagateRange(HttpRequestMessage request)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return;

        // An explicitly set outbound range always wins.
        if (request.Headers.Range is null)
            Copy(context, request, HeaderNames.Range);

        if (!request.Headers.Contains(HeaderNames.IfRange))
            Copy(context, request, HeaderNames.IfRange);
    }

    /// <summary>
    ///     Copies the header verbatim. Parsing it here would only add a second, differently-strict
    ///     interpretation of a value the remote's own range logic is about to interpret anyway; a
    ///     malformed range is ignored there, which is the correct outcome either way. The inbound value
    ///     has already been validated by the server that read it, so it carries no CR/LF.
    /// </summary>
    private static void Copy(HttpContext context, HttpRequestMessage request, string header)
    {
        var value = context.Request.Headers[header].ToString();
        if (!string.IsNullOrEmpty(value))
            request.Headers.TryAddWithoutValidation(header, value);
    }
}
