namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Configuration for the aggregated host health endpoint, which exposes
///     <c>IHostStatus</c> plus every registered <c>IHostHealthContributor</c> through the standard
///     ASP.NET Core health check pipeline.
/// </summary>
public sealed class HostHealthOptions
{
    /// <summary>
    ///     Gets or sets whether the host registers the aggregated health check and maps it.
    ///     Default is <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It was off, and the reason was sound: mapping a route is an opinion about the host's
    ///         route table, and an application that already owns <c>/health</c> would collide. What
    ///         that caution actually produced was a health endpoint <b>no application in this
    ///         repository ever turned on</b> — the contributors were collected on every host and their
    ///         verdict published on none. A bridge nobody crosses is the worse of the two failures.
    ///     </para>
    ///     <para>
    ///         The collision is now handled instead of avoided: <c>HealthEndpointMapper</c> looks at
    ///         the route table and steps aside when the path is taken, saying so. To state that the
    ///         absence is intended rather than accidental, call <c>DisableHealthEndpoint()</c>.
    ///     </para>
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the route the aggregated health endpoint is mapped to.
    ///     Default is <c>/health</c>. Only used when <see cref="Enabled" /> is true.
    /// </summary>
    public string Path { get; set; } = "/health";
}
