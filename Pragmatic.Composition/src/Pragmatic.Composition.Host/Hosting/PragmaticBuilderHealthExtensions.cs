using Pragmatic.Composition;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Extension methods for exposing the aggregated host health via <see cref="IPragmaticBuilder" />.
/// </summary>
public static class PragmaticBuilderHealthExtensions
{
    /// <summary>
    ///     Maps the aggregated host health — the host lifecycle state plus every registered
    ///     <c>IHostHealthContributor</c> — as a standard health check endpoint.
    /// </summary>
    /// <param name="builder">The Pragmatic builder.</param>
    /// <param name="path">The route to map. Defaults to <c>/health</c>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    ///     The endpoint is mapped by default, so this is for <b>moving</b> it: pass the path an
    ///     application wants it on when <c>/health</c> is taken or means something else there. To
    ///     remove it entirely, call <see cref="DisableHealthEndpoint" /> — a call that says the
    ///     absence was decided, where a missing call says nothing at all.
    ///     Only the built-in <see cref="PragmaticBuilder" /> carries the options the generated host
    ///     reads, so a custom <see cref="IPragmaticBuilder" /> implementation is a no-op here.
    /// </remarks>
    public static IPragmaticBuilder UseHealthEndpoint(this IPragmaticBuilder builder, string path = "/health")
    {
        Pragmatic.Ensure.Ensure.ThrowIfNullOrWhiteSpace(path);

        if (builder is PragmaticBuilder concrete)
        {
            concrete.Options.Health.Enabled = true;
            concrete.Options.Health.Path = path;
        }

        return builder;
    }

    /// <summary>
    ///     Stops the host from publishing the aggregated health endpoint.
    /// </summary>
    /// <param name="builder">The Pragmatic builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         For an application that publishes its own health surface and wants no second one, or
    ///         one that must not answer that question at all on this port. The contributors are still
    ///         collected either way — <c>IHostStatus</c> and every <c>IHostHealthContributor</c> keep
    ///         working for anything that reads them in process; what stops is the route.
    ///     </para>
    ///     <para>
    ///         It exists so that "no health endpoint" can be a decision in the source rather than an
    ///         omission. An application that simply never called <c>UseHealthEndpoint()</c> — which
    ///         was every application here — left no way to tell the two apart.
    ///     </para>
    /// </remarks>
    public static IPragmaticBuilder DisableHealthEndpoint(this IPragmaticBuilder builder)
    {
        if (builder is PragmaticBuilder concrete)
            concrete.Options.Health.Enabled = false;

        return builder;
    }
}
