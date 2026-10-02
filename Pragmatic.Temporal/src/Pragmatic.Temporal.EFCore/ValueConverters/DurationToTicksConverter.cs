using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="Duration" /> to ticks (long).
/// </summary>
public sealed class DurationToTicksConverter : ValueConverter<Duration, long>
{
    /// <summary>Creates a new instance.</summary>
    public DurationToTicksConverter()
        : base(
            d => d.Ticks,
            ticks => Duration.FromTicks(ticks))
    {
    }
}
