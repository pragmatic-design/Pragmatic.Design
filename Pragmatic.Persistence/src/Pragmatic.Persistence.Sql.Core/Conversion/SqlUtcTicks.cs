using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Persistence.Sql.Conversion;

/// <summary>A <see cref="DateTimeOffset" /> stored as its UTC ticks in a 64-bit column (the repository's <c>UtcTicksConverter</c>).</summary>
public sealed class SqlUtcTicks : ISqlConverter<DateTimeOffset, long>
{
    public static long ToStore(DateTimeOffset value) => value.UtcTicks;

    public static DateTimeOffset FromStore(long value) => new(value, TimeSpan.Zero);
}
