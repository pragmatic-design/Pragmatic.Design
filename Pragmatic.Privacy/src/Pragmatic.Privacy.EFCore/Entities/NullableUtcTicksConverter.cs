using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Privacy.EFCore.Entities;

/// <summary>
///     Nullable counterpart of <see cref="UtcTicksConverter" />, for timestamps that may be absent —
///     a subject that has not been erased has no erasure time.
/// </summary>
public sealed class NullableUtcTicksConverter() : ValueConverter<DateTimeOffset?, long?>(
    v => v == null ? null : v.Value.UtcTicks,
    v => v == null ? null : new DateTimeOffset(v.Value, TimeSpan.Zero));
