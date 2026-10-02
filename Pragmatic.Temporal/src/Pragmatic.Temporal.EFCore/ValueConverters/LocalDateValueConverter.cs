using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="LocalDate" /> to <see cref="DateOnly" />.
/// </summary>
public sealed class LocalDateValueConverter : ValueConverter<LocalDate, DateOnly>
{
    /// <summary>Creates a new instance of <see cref="LocalDateValueConverter" />.</summary>
    public LocalDateValueConverter()
        : base(
            v => v.ToDateOnly(),
            v => new LocalDate(v))
    {
    }
}

