namespace Pragmatic.Mapping.EFCore;

/// <summary>
///     Telemetry identifiers for the Pragmatic.Mapping.EFCore projection helpers. Exposed so consumers
///     can wire OpenTelemetry without guessing the source name.
/// </summary>
public static class MappingTelemetry
{
    /// <summary>
    ///     The <see cref="System.Diagnostics.ActivitySource" /> name emitted by the projection helpers
    ///     (<c>ToListDtoAsync</c>, <c>ToPagedDtoAsync</c>, …). Register it with
    ///     <c>builder.AddSource(MappingTelemetry.ActivitySourceName)</c>.
    /// </summary>
    public const string ActivitySourceName = "Pragmatic.Mapping";
}
