using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="LocalTime" /> to <see cref="TimeOnly" />.
/// </summary>
public sealed class LocalTimeValueConverter : ValueConverter<LocalTime, TimeOnly>
{
    /// <summary>Creates a new instance.</summary>
    public LocalTimeValueConverter()
        : base(
            v => v.ToTimeOnly(),
            v => new LocalTime(v))
    {
    }
}

