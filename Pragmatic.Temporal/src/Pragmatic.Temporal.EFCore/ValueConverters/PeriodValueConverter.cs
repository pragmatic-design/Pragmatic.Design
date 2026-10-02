using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="Period" />.
///     Stores as ISO 8601 duration string: "P1Y2M3D".
/// </summary>
public sealed class PeriodValueConverter()
    : ValueConverter<Period, string>(period => period.ToString(), str => Period.Parse(str))
{
    // "P1Y2M3D"
}

/// <summary>
///     EF Core value converter for nullable <see cref="Period" />.
/// </summary>
public sealed class NullablePeriodValueConverter() : ValueConverter<Period?, string?>(
    period => period.HasValue ? period.Value.ToString() : null,
    str => string.IsNullOrEmpty(str) ? null : Period.Parse(str));
