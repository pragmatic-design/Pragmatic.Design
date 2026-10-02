using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="DateRange" />.
///     Stores as two separate columns (Start_Date, End_Date) is recommended,
///     but this converter allows storing as a single string "start/end".
/// </summary>
/// <remarks>
///     For better querying, consider using an owned type instead:
///     <code>
///     modelBuilder.Entity&lt;Contract&gt;()
///         .OwnsOne(c => c.ValidityPeriod, range =>
///         {
///             range.Property(r => r.Start).HasColumnName("ValidFrom");
///             range.Property(r => r.End).HasColumnName("ValidTo");
///         });
///     </code>
/// </remarks>
public sealed class DateRangeValueConverter()
    : ValueConverter<DateRange, string>(range => range.ToString(), str => DateRange.Parse(str))
{
    // "2024-01-01/2024-12-31"
}

