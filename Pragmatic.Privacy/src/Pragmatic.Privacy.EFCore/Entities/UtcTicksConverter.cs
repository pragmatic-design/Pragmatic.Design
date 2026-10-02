using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Privacy.EFCore.Entities;

/// <summary>
///     Stores a <see cref="DateTimeOffset" /> as UTC ticks.
/// </summary>
/// <remarks>
///     <para>
///         Not a style choice: several providers — SQLite among them — cannot translate a
///         <see cref="DateTimeOffset" /> comparison into SQL, so a date filter fails at
///         query-compilation time rather than returning wrong rows.
///     </para>
///     <para>
///         <b>Duplicated from Pragmatic.Audit.EFCore on purpose.</b> Depending on the audit module for
///         two value converters would point this module's dependencies in the wrong direction for the
///         sake of forty lines. The right end state is one shared home for EF infrastructure; that is
///         tracked separately, and until then a copy is the smaller mistake.
///     </para>
/// </remarks>
public sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
    v => v.UtcTicks,
    v => new DateTimeOffset(v, TimeSpan.Zero));
