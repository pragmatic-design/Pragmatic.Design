using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition;

namespace Pragmatic.Serialization;

/// <summary>
///     <see cref="IPragmaticBuilder"/> extensions for configuring the shared JSON serialization seam.
/// </summary>
public static class PragmaticJsonBuilderExtensions
{
    /// <summary>
    ///     Configures the shared <see cref="PragmaticJsonOptions"/> used by every Pragmatic
    ///     serialization boundary. Register source-generated contexts and, for AOT, disable the
    ///     reflection fallback:
    ///     <code>
    /// app.UseJson(json =>
    /// {
    ///     json.AddContext(AppJsonContext.Default);
    ///     json.DisableReflectionFallback();
    /// });
    ///     </code>
    /// </summary>
    public static IPragmaticBuilder UseJson(this IPragmaticBuilder builder, Action<PragmaticJsonOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        // Configure the same singleton instance the modules resolve. It stays mutable until the
        // first Build() at serialization time, so configuration order across modules is irrelevant.
        var options = PragmaticJsonServiceCollectionExtensions.GetOrAddOptions(builder.Services);
        configure(options);
        return builder;
    }

    /// <summary>
    ///     Registers a source-generated <typeparamref name="TContext"/> with the shared seam.
    ///     Convenience over <c>UseJson(json =&gt; json.AddContext(new TContext()))</c> — the context is
    ///     consulted before the reflection fallback, and turning that fallback off makes the app's
    ///     serialization fully AOT-safe for the types the context covers.
    /// </summary>
    public static IPragmaticBuilder UseJson<TContext>(this IPragmaticBuilder builder)
        where TContext : JsonSerializerContext, new()
        => builder.UseJson(json => json.AddContext(new TContext()));
}
