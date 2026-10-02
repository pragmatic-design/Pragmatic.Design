using Microsoft.AspNetCore.Builder;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     Adds the output cache middleware, without which a shared <c>[ResponseCache]</c> stores nothing.
/// </summary>
/// <remarks>
///     <para>
///         <c>[ResponseCache]</c> with its default location puts <c>CacheOutput(…)</c> on the route, which
///         is metadata: until this middleware runs, nothing is kept and nothing says so. The generated host
///         adds it — with <c>AddOutputCache</c> — when a module declares such a response.
///     </para>
///     <para>
///         Order 95 — after authorization (94), so a request is refused before a cached answer could reach
///         it. ASP.NET Core's default policy does not cache a request that carries credentials, so only
///         anonymous responses are ever shared (<c>PRAG0554</c> says so at build time).
///     </para>
/// </remarks>
public sealed class OutputCacheStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 95;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
        => app.UseOutputCache();
}
