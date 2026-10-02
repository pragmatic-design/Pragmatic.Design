using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for nullable <see cref="Duration" /> to nullable <see cref="TimeSpan" />.
///     Applied by the convention when <see cref="TemporalEfCoreOptions.StoreDurationAsTicks" /> is false.
/// </summary>
public sealed class NullableDurationToTimeSpanConverter : ValueConverter<Duration?, TimeSpan?>
{
    /// <summary>Creates a new instance.</summary>
    public NullableDurationToTimeSpanConverter()
        : base(
            d => d.HasValue ? d.Value.ToTimeSpan() : null,
            ts => ts.HasValue ? Duration.FromTimeSpan(ts.Value) : null)
    {
    }
}
