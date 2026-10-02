using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for nullable <see cref="Duration" /> to nullable ticks.
/// </summary>
public sealed class NullableDurationToTicksConverter : ValueConverter<Duration?, long?>
{
    /// <summary>Creates a new instance.</summary>
    public NullableDurationToTicksConverter()
        : base(
            d => d.HasValue ? d.Value.Ticks : null,
            ticks => ticks.HasValue ? Duration.FromTicks(ticks.Value) : null)
    {
    }
}
