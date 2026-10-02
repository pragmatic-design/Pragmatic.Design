using Pragmatic.Telemetry;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Configuration options for Pragmatic application hosting.
/// </summary>
public sealed class PragmaticOptions
{
    /// <summary>
    ///     Gets the maintenance mode configuration.
    /// </summary>
    public MaintenanceModeOptions MaintenanceMode { get; } = new();

    /// <summary>
    ///     Gets the OpenTelemetry configuration. Enabled by default with sensible defaults.
    /// </summary>
    public TelemetryOptions Telemetry { get; } = new();

    /// <summary>
    ///     Gets the aggregated host health endpoint configuration. Disabled by default —
    ///     opt in with <c>UseHealthEndpoint()</c>.
    /// </summary>
    public HostHealthOptions Health { get; } = new();

    /// <summary>
    ///     Gets or sets whether to automatically run database migrations on startup.
    ///     Uses EF Core <c>MigrateAsync</c> on MigrationDbContext types.
    ///     Default is false. Mutually exclusive with <see cref="EnsureDatabaseCreated" />.
    /// </summary>
    public bool AutoMigrations { get; set; }

    /// <summary>
    ///     Gets or sets whether to call EF Core <c>EnsureCreatedAsync</c> on startup.
    ///     Creates tables from the model without migrations — intended for development only.
    ///     Default is false. Mutually exclusive with <see cref="AutoMigrations" />.
    /// </summary>
    public bool EnsureDatabaseCreated { get; set; }

    /// <summary>
    ///     Gets or sets whether to enable detailed error messages.
    ///     Default is true in Development, false otherwise.
    /// </summary>
    public bool? DetailedErrors { get; set; }
}

