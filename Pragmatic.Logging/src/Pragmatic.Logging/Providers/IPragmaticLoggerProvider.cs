using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Represents a logging provider that supports Pragmatic.Logging features like 
/// structured logging, context enrichment, and granular configuration.
/// </summary>
public interface IPragmaticLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// Gets the provider name for identification and configuration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the provider configuration that controls what and how to log.
    /// </summary>
    IPragmaticProviderConfiguration Configuration { get; }

    /// <summary>
    /// Updates the provider configuration at runtime.
    /// </summary>
    /// <param name="configuration">New configuration to apply</param>
    void UpdateConfiguration(IPragmaticProviderConfiguration configuration);

    /// <summary>
    /// Gets provider-specific metrics and statistics.
    /// </summary>
    /// <returns>Provider metrics</returns>
    ProviderMetrics GetMetrics();

    /// <summary>
    /// Performs a health check on the provider.
    /// </summary>
    /// <returns>Health check result</returns>
    ProviderHealthStatus CheckHealth();
}

/// <summary>
/// Configuration interface for Pragmatic.Logging providers with granular control.
/// </summary>
public interface IPragmaticProviderConfiguration
{
    /// <summary>
    /// Gets or sets the minimum log level for this provider.
    /// </summary>
    LogLevel MinimumLevel { get; set; }

    /// <summary>
    /// Gets or sets category-specific log level overrides.
    /// </summary>
    Dictionary<string, LogLevel> CategoryLevels { get; set; }

    /// <summary>
    /// Gets or sets whether to include structured properties.
    /// </summary>
    bool IncludeStructuredProperties { get; set; }

    /// <summary>
    /// Gets or sets whether to include context enrichment.
    /// </summary>
    bool IncludeContextEnrichment { get; set; }

    /// <summary>
    /// Gets or sets which context properties to include/exclude.
    /// </summary>
    ContextFilterConfiguration ContextFilter { get; set; }

    /// <summary>
    /// Gets or sets formatting options for this provider.
    /// </summary>
    FormattingConfiguration Formatting { get; set; }

    /// <summary>
    /// Gets or sets performance and batching options.
    /// </summary>
    PerformanceConfiguration Performance { get; set; }

    /// <summary>
    /// Gets or sets custom properties specific to this provider.
    /// </summary>
    Dictionary<string, object?> CustomProperties { get; set; }

    /// <summary>
    /// Gets or sets the filter configuration for advanced filtering.
    /// </summary>
    FilterConfiguration Filters { get; set; }

    /// <summary>
    /// Gets or sets the filter expression for advanced expression-based filtering.
    /// When set, this takes precedence over the Filters configuration.
    /// </summary>
    FilterExpression? FilterExpression { get; set; }

    /// <summary>
    /// Gets or sets the privacy configuration for automatic data redaction.
    /// </summary>
    PrivacyConfiguration Privacy { get; set; }

    /// <summary>
    /// Validates the configuration and returns any validation errors.
    /// </summary>
    /// <returns>List of validation errors, empty if valid</returns>
    IEnumerable<string> Validate();
}
