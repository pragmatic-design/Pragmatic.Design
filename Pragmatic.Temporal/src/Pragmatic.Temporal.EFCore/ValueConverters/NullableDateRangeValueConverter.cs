using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for nullable <see cref="DateRange" />.
/// </summary>
public sealed class NullableDateRangeValueConverter : ValueConverter<DateRange?, string?>
{
    /// <summary>Creates a new instance.</summary>
    public NullableDateRangeValueConverter() : base(
        range => range.HasValue ? range.Value.ToString() : null,
        str => string.IsNullOrEmpty(str) ? null : DateRange.Parse(str))
    {
    }
}
