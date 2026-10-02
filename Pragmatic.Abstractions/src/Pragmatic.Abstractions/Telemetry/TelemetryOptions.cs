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
}
