namespace Pragmatic.Composition.Remote;

/// <summary>
///     Trust-boundary configuration for the generated <c>/_pragmatic/invoke</c> endpoint.
///     Bound from configuration section <see cref="ConfigurationSection"/>.
/// </summary>
/// <remarks>
///     The invoke endpoint is internal boundary-to-boundary RPC. It is <b>fail-closed by default</b>:
///     unless overridden here, it requires an authenticated principal. A trusted-network deployment
///     may opt out <b>explicitly</b> via <see cref="AllowAnonymous"/>; it is never silently anonymous.
/// </remarks>
public sealed class PragmaticRemoteInvokeOptions
{
    /// <summary>Configuration section bound to these options.</summary>
    public const string ConfigurationSection = "Pragmatic:RemoteBoundaries:InvokeEndpoint";

    /// <summary>
    ///     When <c>true</c>, the invoke endpoint is mapped with <c>AllowAnonymous</c> — an explicit
    ///     opt-out for trusted-network deployments. Default <c>false</c> (fail-closed).
    /// </summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>
    ///     Named authorization policy enforced on the invoke endpoint. When set (and
    ///     <see cref="AllowAnonymous"/> is <c>false</c>), the endpoint requires this policy.
    ///     When <c>null</c>/empty, the endpoint requires the default authenticated-user policy.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }
}
