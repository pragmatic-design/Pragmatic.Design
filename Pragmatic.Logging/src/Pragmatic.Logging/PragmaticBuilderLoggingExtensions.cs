using Pragmatic.Composition;
using Pragmatic.Logging.Extensions;

namespace Pragmatic.Logging.Extensions;

/// <summary>
///     Extension methods for configuring Pragmatic.Logging on <see cref="IPragmaticBuilder" />.
/// </summary>
public static class PragmaticBuilderLoggingExtensions
{
    /// <summary>
    ///     Configures Pragmatic logging providers (Console, File).
    /// </summary>
    /// <param name="builder">The Pragmatic builder.</param>
    /// <param name="configure">Action to configure the logging builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IPragmaticBuilder UseLogging(
        this IPragmaticBuilder builder,
        Action<PragmaticLoggingBuilder> configure)
    {
        builder.Services.AddPragmaticLoggingBuilder(configure);
        return builder;
    }
}
