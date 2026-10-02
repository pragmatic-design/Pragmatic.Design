using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Audit.EFCore.Entities;

/// <summary>
///     Stores a <see cref="DateTimeOffset" /> as UTC ticks.
/// </summary>
/// <remarks>
///     <para>
///         Not a stylistic choice. Several providers — SQLite among them — cannot translate a
///         <see cref="DateTimeOffset" /> comparison into SQL at all, so <c>WHERE OccurredAt &gt;= @from</c>
///         fails at query-compilation time. Every time filter the trail offers would be unusable there,
///         which for an audit trail means most of its queries.
///     </para>
///     <para>
///         An integer comparison translates everywhere, sorts correctly, and indexes better than text.
///         Nothing is lost: the trail is UTC throughout, so there is no offset worth preserving.
///     </para>
/// </remarks>
public sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
    v => v.UtcTicks,
    v => new DateTimeOffset(v, TimeSpan.Zero));
