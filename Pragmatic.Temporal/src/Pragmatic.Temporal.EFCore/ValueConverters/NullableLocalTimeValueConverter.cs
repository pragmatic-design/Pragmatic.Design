using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for nullable <see cref="LocalTime" /> to nullable <see cref="TimeOnly" />.
/// </summary>
public sealed class NullableLocalTimeValueConverter : ValueConverter<LocalTime?, TimeOnly?>
{
    /// <summary>Creates a new instance.</summary>
    public NullableLocalTimeValueConverter()
        : base(
            v => v.HasValue ? v.Value.ToTimeOnly() : null,
            v => v.HasValue ? new LocalTime(v.Value) : null)
    {
    }
}
