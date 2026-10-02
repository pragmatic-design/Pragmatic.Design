using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Resilience.Configuration;

/// <summary>
///     Reports at startup every declared <c>[ResiliencePolicy]</c> name that nothing defines.
/// </summary>
/// <remarks>
///     <para>
///         Such a name resolves to the passthrough pipeline: the call runs with no retry, no breaker
///         and no timeout. That is allowed — annotating first and configuring later is a supported
///         workflow — but it is not silent: a typo in the attribute, or a <c>Resilience</c> section missing
///         from the production settings, is one warning per name at startup instead of a discovery at
///         the first outage.
///     </para>
///     <para>
///         The generated host calls this after the application is built. The names come from the
///         <see cref="DeclaredResiliencePolicy" /> entries each module's generated registration adds; the
///         definitions are what <see cref="ResiliencePipelineProvider" /> would find at that moment. A
///         policy an application adds fluently later than that is reported, and then used.
///     </para>
/// </remarks>
public static partial class UndefinedResiliencePolicies
{
    /// <summary>
    ///     Logs one warning per declared policy name nothing defines, and returns those names.
    /// </summary>
    public static IReadOnlyList<string> Report(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var provider = services.GetRequiredService<ResiliencePipelineProvider>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(UndefinedResiliencePolicies));

        var undefined = new List<string>();
        foreach (var byName in services.GetServices<DeclaredResiliencePolicy>()
                     .GroupBy(d => d.PolicyName, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (provider.IsDefined(byName.Key))
                continue;

            undefined.Add(byName.Key);
            LogUndefinedPolicy(logger, byName.Key,
                string.Join(", ", byName.Select(d => d.Operation).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
        }

        return undefined;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Resilience policy '{PolicyName}' is declared by {Operations} and nothing defines it: no "
                  + "Resilience:Policies:{PolicyName} and no Resilience:Default. It runs as a passthrough — no "
                  + "retry, no circuit breaker, no timeout.")]
    private static partial void LogUndefinedPolicy(ILogger logger, string policyName, string operations);
}
