using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="Duration" /> to <see cref="TimeSpan" />.
///     Use this for databases that support TimeSpan natively
///     (or set <see cref="TemporalEfCoreOptions.StoreDurationAsTicks" /> to false).
/// </summary>
public sealed class DurationToTimeSpanConverter : ValueConverter<Duration, TimeSpan>
{
    /// <summary>Creates a new instance.</summary>
    public DurationToTimeSpanConverter()
        : base(
            d => d.ToTimeSpan(),
            ts => Duration.FromTimeSpan(ts))
    {
    }
}
