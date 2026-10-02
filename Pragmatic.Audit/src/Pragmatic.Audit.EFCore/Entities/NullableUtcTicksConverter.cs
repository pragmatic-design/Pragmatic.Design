using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Audit.EFCore.Entities;

/// <summary>
///     Nullable counterpart of <see cref="UtcTicksConverter" />, for timestamps that may be absent —
///     an unsealed segment has no seal time.
/// </summary>
public sealed class NullableUtcTicksConverter() : ValueConverter<DateTimeOffset?, long?>(
    v => v == null ? null : v.Value.UtcTicks,
    v => v == null ? null : new DateTimeOffset(v.Value, TimeSpan.Zero));
