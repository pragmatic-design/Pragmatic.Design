using Microsoft.Extensions.Configuration;

namespace Pragmatic.Telemetry;

/// <summary>
///     Configuration options for Pragmatic telemetry (traces, metrics, logs).
///     Used by the generated <c>PragmaticApp</c> host to configure OpenTelemetry.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>
    ///     Master switch. When false, no OTel providers are registered.
    ///     Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Enable distributed tracing (TracerProvider). Default: true.
    /// </summary>
    public bool Tracing { get; set; } = true;

    /// <summary>
    ///     Enable metrics collection (MeterProvider). Default: true.
    /// </summary>
    public bool Metrics { get; set; } = true;

    /// <summary>
    ///     Enable OTel logging bridge (ILogger → OTLP export). Default: true.
    /// </summary>
    public bool Logging { get; set; } = true;

    /// <summary>
    ///     Enable OTLP exporter for production environments.
    ///     When false and in Development, a console exporter is used instead.
    ///     Default: false.
    /// </summary>
    public bool UseOtlpExporter { get; set; }

    /// <summary>
    ///     Override the service name used in OTel resource.
    ///     When null, the assembly name is used.
    /// </summary>
    public string? ServiceName { get; set; }

    private double _samplingRatio = 0.1;

    /// <summary>
    ///     Trace sampling ratio for production (0.0 to 1.0).
    ///     In Development, sampling is always 1.0 (all traces).
    ///     Default: 0.1 (10%).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when set outside the inclusive range [0.0, 1.0]. An out-of-range ratio
    ///     would otherwise be passed straight to the OpenTelemetry SDK, which clamps or
    ///     misbehaves silently depending on version — fail fast at configuration time instead.
    /// </exception>
    public double SamplingRatio
    {
        get => _samplingRatio;
        set
        {
            if (value is < 0.0 or > 1.0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "SamplingRatio must be in the inclusive range [0.0, 1.0].");
            _samplingRatio = value;
        }
    }

    /// <summary>
    ///     Reads the options from a configuration section (the <c>Telemetry</c> section of the generated host), key by key.
    /// </summary>
    /// <param name="section">The section; a key it does not hold leaves the property as it is.</param>
    /// <exception cref="InvalidOperationException">A value that is not one of its property's type, named by its path.</exception>
    /// <remarks>
    ///     What <c>ConfigurationBinder.Bind</c> read, without its reflection: keys in any case, values in the invariant culture. The
    ///     generated host calls this, and a call to <c>Bind</c> there could not be made AOT-safe, because ASP.NET's binding generator
    ///     never sees code another generator wrote.
    /// </remarks>
    public void ReadFrom(IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);

        Enabled = ReadBool(section, nameof(Enabled)) ?? Enabled;
        Tracing = ReadBool(section, nameof(Tracing)) ?? Tracing;
        Metrics = ReadBool(section, nameof(Metrics)) ?? Metrics;
        Logging = ReadBool(section, nameof(Logging)) ?? Logging;
        UseOtlpExporter = ReadBool(section, nameof(UseOtlpExporter)) ?? UseOtlpExporter;
        ServiceName = section[nameof(ServiceName)] ?? ServiceName;

        if (section[nameof(SamplingRatio)] is { } ratio)
        {
            SamplingRatio = double.TryParse(ratio, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw Unreadable(section, nameof(SamplingRatio), ratio, typeof(double));
        }
    }

    private static bool? ReadBool(IConfiguration section, string key)
        => section[key] is not { } value ? null
            : bool.TryParse(value, out var parsed) ? parsed
            : throw Unreadable(section, key, value, typeof(bool));

    private static InvalidOperationException Unreadable(IConfiguration section, string key, string value, Type type)
        => new($"Failed to convert configuration value '{value}' at '{PathOf(section, key)}' to type '{type}'.");

    private static string PathOf(IConfiguration section, string key)
        => section is IConfigurationSection { Path: { Length: > 0 } path } ? $"{path}:{key}" : key;
}
