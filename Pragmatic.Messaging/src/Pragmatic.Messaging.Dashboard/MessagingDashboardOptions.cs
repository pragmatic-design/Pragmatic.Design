namespace Pragmatic.Messaging.Dashboard;

/// <summary>
///     Options for the messaging operational dashboard.
///     Auth mirrors the maintenance panel: with <see cref="ApiKey"/> set, requests must carry
///     <c>X-Messaging-Key</c> (compared constant-time); without one, access is localhost-only.
/// </summary>
public sealed class MessagingDashboardOptions
{
    /// <summary>Base path of the dashboard group. Default: <c>/_messaging</c>.</summary>
    public string Path { get; set; } = "/_messaging";

    /// <summary>API key required in the <c>X-Messaging-Key</c> header. Null = localhost-only.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    ///     Additional labelled API keys (<c>label → key</c>). Any of these — or <see cref="ApiKey"/> —
    ///     is accepted in <c>X-Messaging-Key</c>; the matching label is logged on replay/delete so an
    ///     operation is attributable to a caller. Issue one key per operator and rotate a single key
    ///     without a redeploy. When empty and <see cref="ApiKey"/> is null, the dashboard is loopback-only.
    /// </summary>
    public Dictionary<string, string> ApiKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>Maximum items returned by list endpoints (outbox, dead-letters, audit). Default: 100.</summary>
    public int MaxItems { get; set; } = 100;

    /// <summary>
    ///     Header whose presence is required on mutating requests (replay/delete) in loopback (no-key)
    ///     mode. Being a non-simple header it forces a CORS preflight, which same-origin policy blocks
    ///     for a cross-site page — defeating a drive-by CSRF POST to <c>localhost</c>. In keyed mode the
    ///     <c>X-Messaging-Key</c> header already forces the preflight, so this is not additionally required.
    /// </summary>
    public const string CsrfHeader = "X-Messaging-Csrf";
}
