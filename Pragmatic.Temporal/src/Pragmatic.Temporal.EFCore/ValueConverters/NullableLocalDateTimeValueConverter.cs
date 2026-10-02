using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for nullable <see cref="LocalDateTime" />.
/// </summary>
public sealed class NullableLocalDateTimeValueConverter : ValueConverter<LocalDateTime?, DateTime?>
{
    /// <summary>Creates a new instance.</summary>
    public NullableLocalDateTimeValueConverter()
        : base(
            v => v.HasValue ? v.Value.ToDateTime() : null,
            v => v.HasValue ? new LocalDateTime(DateTime.SpecifyKind(v.Value, DateTimeKind.Unspecified)) : null)
    {
    }
}
