using System.Net;
using System.Net.Sockets;

namespace Pragmatic.Http;

/// <summary>
///     Rejects outbound URLs that point back into the network the application is running in.
/// </summary>
/// <remarks>
///     <para>
///         For the case where an allowlist is not available: a URL supplied by a user, a tenant, or a
///         configuration file someone else edits. Where the set of legitimate hosts <em>is</em> known —
///         Slack's <c>hooks.slack.com</c>, for instance — an allowlist is strictly stronger and should
///         be preferred; this is the fallback, not the first choice.
///     </para>
///     <para>
///         <b>What it is actually defending.</b> A server that fetches a URL on request will happily
///         fetch <c>http://169.254.169.254/</c> and hand back cloud credentials, or reach an internal
///         admin endpoint that is only unprotected because it was assumed unreachable. The attacker
///         supplies the address; the server supplies the position on the network.
///     </para>
///     <para>
///         ⚠️ <b>This cannot close SSRF on its own, and pretending otherwise is the real danger.</b>
///         Checking a name means resolving it, and the resolution that matters is the one the HTTP stack
///         performs when it connects — a name can answer with a public address here and a private one a
///         moment later (DNS rebinding). Treat this as removing the easy cases; a deployment that must
///         actually hold uses an egress proxy or network policy, where the decision is made at connect
///         time.
///     </para>
/// </remarks>
public static class OutboundUrlGuard
{
    /// <summary>Why a URL was refused. <see cref="OutboundUrlVerdict.Allowed" /> means it passed.</summary>
    public static OutboundUrlVerdict Inspect(string? url, bool allowHttp = false)
        => !Uri.TryCreate(url, UriKind.Absolute, out var uri) || IsImplicitFilePath(url!, uri)
            ? OutboundUrlVerdict.NotAnAbsoluteUrl
            : Inspect(uri, allowHttp);

    /// <summary>
    ///     A path the platform turned into a file URI on its own. On Linux and macOS
    ///     <c>Uri.TryCreate("/relative/path", UriKind.Absolute, …)</c> succeeds with file:///relative/path,
    ///     on Windows it fails, so the verdict for the same input depended on the OS the check ran on.
    ///     A path is not a URL anyone wrote; only an explicit file: scheme reaches the scheme check.
    /// </summary>
    private static bool IsImplicitFilePath(string url, Uri uri)
        => uri.IsFile && !url.TrimStart().StartsWith("file:", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc cref="Inspect(string?, bool)" />
    public static OutboundUrlVerdict Inspect(Uri? uri, bool allowHttp = false)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            return OutboundUrlVerdict.NotAnAbsoluteUrl;

        // Scheme first. file:// reads the server's disk and gopher:// and friends have been used to
        // speak entirely different protocols through a URL field.
        var schemeAllowed = uri.Scheme == Uri.UriSchemeHttps
            || (allowHttp && uri.Scheme == Uri.UriSchemeHttp);

        if (!schemeAllowed)
            return OutboundUrlVerdict.SchemeNotAllowed;

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return OutboundUrlVerdict.CredentialsInUrl;

        // A literal address skips resolution entirely, so check it directly.
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal))
            return IsInternal(literal) ? OutboundUrlVerdict.ResolvesToInternalAddress : OutboundUrlVerdict.Allowed;

        return OutboundUrlVerdict.Allowed;
    }

    /// <summary>
    ///     Resolves the host and refuses it if any address is internal.
    /// </summary>
    /// <remarks>
    ///     <b>Any, not all.</b> A name answering with one public and one private address must be refused:
    ///     accepting it would let the connection pick the private one, which is the whole trick.
    ///     Resolution failure is also a refusal — a host that cannot be resolved cannot be shown safe,
    ///     and defaulting to "allow" on error is how these checks stop working without anyone noticing.
    /// </remarks>
    public static async ValueTask<OutboundUrlVerdict> InspectResolvedAsync(
        Uri? uri, bool allowHttp = false, CancellationToken ct = default)
    {
        var verdict = Inspect(uri, allowHttp);
        if (verdict != OutboundUrlVerdict.Allowed || uri is null)
            return verdict;

        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out _))
            return verdict;   // already checked as a literal

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return OutboundUrlVerdict.HostCouldNotBeResolved;
        }
        catch (ArgumentException)
        {
            // The resolver rejects some inputs outright rather than failing to find them:
            // GetHostAddressesAsync("0.0.0.0") throws ArgumentException (measured). No input can reach
            // it that way today — the TryParse above returns every literal before this line, and a
            // non-literal name fails as SocketException (also measured, across long, underscored,
            // hyphen-edged and non-ASCII hosts). So this catch has NO KNOWN TRIGGER and carries no test.
            //
            // It stays because callers place this guard outside their delivery try/catch: an escaping
            // exception would leave their method instead of becoming a refusal, and the cost of being
            // wrong about "unreachable" is a failed operation rather than a refused URL.
            return OutboundUrlVerdict.HostCouldNotBeResolved;
        }

        if (addresses.Length == 0)
            return OutboundUrlVerdict.HostCouldNotBeResolved;

        foreach (var address in addresses)
            if (IsInternal(address))
                return OutboundUrlVerdict.ResolvesToInternalAddress;

        return OutboundUrlVerdict.Allowed;
    }

    /// <summary>
    ///     Whether an address belongs to the network the application sits in rather than the internet.
    /// </summary>
    private static bool IsInternal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 => true,                          // 0.0.0.0/8 — "this network"
                10 => true,                         // 10/8
                127 => true,                        // covered by IsLoopback, kept explicit
                169 when b[1] == 254 => true,       // link-local, and the cloud metadata endpoint
                172 when b[1] is >= 16 and <= 31 => true,
                192 when b[1] == 168 => true,
                100 when b[1] is >= 64 and <= 127 => true,   // carrier-grade NAT
                >= 224 => true,                     // multicast and reserved
                _ => false,
            };
        }

        return address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast
            || IsIPv6UniqueLocal(address)
            || address.Equals(IPAddress.IPv6Any);
    }

    /// <summary>fc00::/7 — the IPv6 equivalent of the private ranges, which .NET has no property for.</summary>
    private static bool IsIPv6UniqueLocal(IPAddress address)
        => (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
}
