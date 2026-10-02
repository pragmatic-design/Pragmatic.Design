using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="LocalDateTime" /> to <see cref="DateTime" />.
///     The DateTime is stored without any timezone conversion.
/// </summary>
public sealed class LocalDateTimeValueConverter : ValueConverter<LocalDateTime, DateTime>
{
    /// <summary>Creates a new instance.</summary>
    public LocalDateTimeValueConverter()
        : base(
            v => v.ToDateTime(),
            v => new LocalDateTime(DateTime.SpecifyKind(v, DateTimeKind.Unspecified)))
    {
    }
}

