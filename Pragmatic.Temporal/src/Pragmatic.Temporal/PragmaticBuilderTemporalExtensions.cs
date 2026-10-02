using Pragmatic.Composition;
using Pragmatic.Temporal.Extensions;

namespace Pragmatic.Temporal;

/// <summary>
///     Extension methods for configuring Temporal on <see cref="IPragmaticBuilder"/>.
/// </summary>
public static class PragmaticBuilderTemporalExtensions
{
    /// <summary>
    ///     Configures temporal services for the application.
    /// </summary>
    /// <param name="builder">The Pragmatic builder.</param>
    /// <param name="configure">Action to configure Temporal via the builder.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <example>
    /// <code>
    /// app.UseTemporal(temporal =>
    /// {
    ///     temporal.UseDefaultTimeZone("Europe/Rome");
    ///     temporal.UseHolidayProvider&lt;ItalianHolidayProvider&gt;();
    /// });
    /// </code>
    /// </example>
    public static IPragmaticBuilder UseTemporal(
        this IPragmaticBuilder builder,
        Action<TemporalBuilder> configure)
    {
        builder.Services.AddPragmaticTemporal();
        var temporalBuilder = new TemporalBuilder(builder.Services);
        configure(temporalBuilder);
        return builder;
    }
}
