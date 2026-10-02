using Pragmatic.Logging.Filtering;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// Global filter configuration that applies to all providers before provider-specific filtering.
/// This enables centralized filtering to reduce processing overhead and eliminate duplication.
/// </summary>
public sealed class GlobalFilterConfiguration
{
    /// <summary>
    /// Gets or sets the global filter expression that applies to all providers.
    /// Common use case: f => !f.HealthCheck() &amp;&amp; !f.MonitoringTools() &amp;&amp; f.RateLimit(TimeSpan.FromMinutes(1), 1000)
    /// </summary>
    public FilterExpression? FilterExpression { get; set; }

    /// <summary>
    /// Gets or sets the legacy filter configuration for backward compatibility.
    /// When FilterExpression is set, this is ignored.
    /// </summary>
    public FilterConfiguration Filters { get; set; } = new();

    /// <summary>
    /// Gets or sets whether global filtering is enabled.
    /// When false, all log entries pass through to provider-specific filtering.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to collect metrics about global filter performance.
    /// Useful for monitoring and optimizing filter expressions.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets custom properties for global filter configuration.
    /// </summary>
    public Dictionary<string, object?> CustomProperties { get; set; } = new();

    /// <summary>
    /// Creates a global filter configuration with common exclusions.
    /// </summary>
    /// <returns>Configuration that excludes health checks, monitoring, and applies rate limiting</returns>
    public static GlobalFilterConfiguration CreateDefault()
    {
        return new GlobalFilterConfiguration
        {
            FilterExpression = f =>
                !f.HealthCheck() &&
                !f.MonitoringTools() &&
                f.RateLimit(TimeSpan.FromMinutes(1), 1000)
        };
    }

    /// <summary>
    /// Creates a global filter configuration for development environments.
    /// More permissive but still excludes obvious noise.
    /// </summary>
    /// <returns>Configuration suitable for development</returns>
    public static GlobalFilterConfiguration ForDevelopment()
    {
        return new GlobalFilterConfiguration
        {
            FilterExpression = f =>
                !f.HealthCheck() &&
                f.RateLimit(TimeSpan.FromSeconds(10), 100)
        };
    }

    /// <summary>
    /// Creates a global filter configuration for production environments.
    /// More restrictive to reduce log volume and focus on important events.
    /// </summary>
    /// <returns>Configuration suitable for production</returns>
    public static GlobalFilterConfiguration ForProduction()
    {
        return new GlobalFilterConfiguration
        {
            FilterExpression = f =>
                !f.HealthCheck() &&
                !f.MonitoringTools() &&
                f.RateLimit(TimeSpan.FromMinutes(5), 500) &&
                (f.Level(Microsoft.Extensions.Logging.LogLevel.Warning) || f.BusinessCritical())
        };
    }

    /// <summary>
    /// Creates a global filter configuration that excludes all noise but allows everything else.
    /// </summary>
    /// <returns>Configuration that only excludes common noise</returns>
    public static GlobalFilterConfiguration ExcludeNoiseOnly()
    {
        return new GlobalFilterConfiguration
        {
            FilterExpression = f =>
                !f.HealthCheck() &&
                !f.MonitoringTools()
        };
    }

    /// <summary>
    /// Creates a disabled global filter configuration (passes everything through).
    /// </summary>
    /// <returns>Configuration that allows all log entries</returns>
    public static GlobalFilterConfiguration Disabled()
    {
        return new GlobalFilterConfiguration
        {
            Enabled = false
        };
    }

    /// <summary>
    /// Validates the global filter configuration.
    /// </summary>
    /// <returns>Validation errors if any</returns>
    public IEnumerable<string> Validate()
    {
        var errors = new List<string>();

        // If both FilterExpression and Filters are set, warn about precedence
        if (FilterExpression != null && Filters.Filters.Any())
        {
            errors.Add("Both FilterExpression and Filters are configured. FilterExpression takes precedence.");
        }

        // Try to compile the expression to validate it
        if (FilterExpression != null)
        {
            try
            {
                var compiled = FilterExpressionEvaluator.Compile(FilterExpression);
                if (!compiled.IsValid)
                {
                    errors.Add($"FilterExpression compilation failed: {compiled.CompilationException?.Message}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"FilterExpression validation failed: {ex.Message}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Creates a copy of this configuration.
    /// </summary>
    /// <returns>Deep copy of the configuration</returns>
    public GlobalFilterConfiguration Clone()
    {
        return new GlobalFilterConfiguration
        {
            FilterExpression = FilterExpression,
            Filters = new FilterConfiguration(), // Create new instance for filters
            Enabled = Enabled,
            EnableMetrics = EnableMetrics,
            CustomProperties = new Dictionary<string, object?>(CustomProperties)
        };
    }

    /// <summary>
    /// Returns a string representation for debugging.
    /// </summary>
    /// <returns>String representation</returns>
    public override string ToString()
    {
        var status = Enabled ? "Enabled" : "Disabled";
        var filterType = FilterExpression != null ? "Expression" :
                        Filters.Filters.Any() ? "Legacy" : "None";
        return $"GlobalFilterConfiguration({status}, {filterType})";
    }
}