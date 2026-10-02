using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for nullable <see cref="LocalDate" /> to nullable <see cref="DateOnly" />.
/// </summary>
public sealed class NullableLocalDateValueConverter : ValueConverter<LocalDate?, DateOnly?>
{
    /// <summary>Creates a new instance.</summary>
    public NullableLocalDateValueConverter()
        : base(
            v => v.HasValue ? v.Value.ToDateOnly() : null,
            v => v.HasValue ? new LocalDate(v.Value) : null)
    {
    }
}
