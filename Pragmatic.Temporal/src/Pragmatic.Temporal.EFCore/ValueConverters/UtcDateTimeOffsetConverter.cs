using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     Stores a <see cref="DateTimeOffset" /> as the same instant in UTC.
/// </summary>
/// <remarks>
///     The offset a caller happened to write is not part of the instant, and keeping it in the column
///     leaves every later read converting outward from a value whose meaning depends on who wrote it.
///     Reading back is the identity: what was stored is already UTC.
/// </remarks>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    /// <summary>Creates a new instance.</summary>
    public UtcDateTimeOffsetConverter()
        : base(
            value => value.ToUniversalTime(),
            stored => stored)
    {
    }
}
