using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="ZonedDateTime" /> to ISO 8601 string with timezone bracket.
///     Stores as "2024-01-15T10:30:00+01:00[Europe/Rome]".
/// </summary>
public sealed class ZonedDateTimeValueConverter : ValueConverter<ZonedDateTime, string>
{
    /// <summary>Creates a new instance.</summary>
    public ZonedDateTimeValueConverter()
        : base(
            v => v.ToString(),
            s => ZonedDateTime.Parse(s))
    {
    }
}
