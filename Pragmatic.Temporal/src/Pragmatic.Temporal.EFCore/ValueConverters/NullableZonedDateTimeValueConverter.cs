using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for nullable <see cref="ZonedDateTime" /> to nullable string.
/// </summary>
public sealed class NullableZonedDateTimeValueConverter : ValueConverter<ZonedDateTime?, string?>
{
    /// <summary>Creates a new instance.</summary>
    public NullableZonedDateTimeValueConverter()
        : base(
            v => v.HasValue ? v.Value.ToString() : null,
            s => !string.IsNullOrEmpty(s) ? ZonedDateTime.Parse(s) : null)
    {
    }
}
