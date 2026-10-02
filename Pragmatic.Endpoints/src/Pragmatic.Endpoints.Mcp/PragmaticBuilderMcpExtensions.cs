using Pragmatic.Composition;

namespace Pragmatic.Endpoints.Mcp;

/// <summary>
///     Turns on MCP exposure from the Pragmatic builder: <c>app.UseMcp()</c> makes every
///     <c>[McpTool]</c> endpoint an MCP tool.
/// </summary>
/// <remarks>
///     <para>
///         The same registration <see cref="PragmaticMcpExtensions.AddPragmaticMcp" /> makes, under the
///         name the rest of the framework uses for a host-level choice — <c>Use{Module}()</c> on
///         <see cref="IPragmaticBuilder" />. Both stay: a library that only has an
///         <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection" /> has no builder to
///         call this on.
///     </para>
///     <para>
///         ⚠️ <c>[McpTool]</c>'s own documentation says the endpoint is exposed «when the host enables
///         <c>UseMcp()</c>», and every other module keeps the same convention: a reader following
///         either writes this call, so it has to exist.
///     </para>
/// </remarks>
public static class PragmaticBuilderMcpExtensions
{
    /// <summary>Registers the MCP server, the tool catalog and executor, and the pipeline step.</summary>
    /// <param name="builder">The Pragmatic builder.</param>
    /// <param name="configure">Optional configuration — route, authorization, forwarded headers.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <example>
    ///     <code>
    ///     await PragmaticApp.RunAsync(args, app =>
    ///     {
    ///         app.UseMcp();                                  // /mcp, authorization required
    ///         app.UseMcp(o => o.RequireAuthorization = false); // or behind a trusted gateway
    ///     });
    ///     </code>
    /// </example>
    public static IPragmaticBuilder UseMcp(
        this IPragmaticBuilder builder, Action<McpOptions>? configure = null)
    {
        Ensure.Ensure.ThrowIfNull(builder);

        builder.Services.AddPragmaticMcp(configure);
        return builder;
    }
}
