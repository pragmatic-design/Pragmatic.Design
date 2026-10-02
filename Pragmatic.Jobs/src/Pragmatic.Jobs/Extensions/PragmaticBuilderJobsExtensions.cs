using Pragmatic.Composition;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Extensions;

namespace Pragmatic.Jobs;

/// <summary>
///     Extension methods for configuring Jobs on <see cref="IPragmaticBuilder"/>.
/// </summary>
public static class PragmaticBuilderJobsExtensions
{
    /// <summary>
    ///     Configures background job processing for the application.
    /// </summary>
    /// <param name="builder">The Pragmatic builder.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <example>
    /// <code>
    /// app.UseJobs(jobs =>
    /// {
    ///     jobs.WithWorkerCount(4);
    ///     jobs.WithPollingInterval(5);
    ///     jobs.UseEfCore();
    /// });
    /// </code>
    /// </example>
    public static IPragmaticBuilder UseJobs(
        this IPragmaticBuilder builder,
        Action<JobsBuilder>? configure = null)
    {
        builder.Services.AddPragmaticJobs(configure);
        return builder;
    }
}
