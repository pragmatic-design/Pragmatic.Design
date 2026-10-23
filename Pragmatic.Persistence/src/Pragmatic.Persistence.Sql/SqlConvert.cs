using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     A column stored as another type than the property's: the two static functions between them. Declared on
///     the property with <see cref="ConvertAttribute{TConverter}" />; the generator calls them in the readers, the
///     binders and the predicates' parameters, so no conversion is looked up at runtime (the repository's
///     <c>UtcTicksConverter</c>, <c>ShortGuid</c>, enum-as-string and value objects are converters of this shape).
/// </summary>
public interface ISqlConverter<TModel, TStore>
{
    static abstract TStore ToStore(TModel value);

    static abstract TModel FromStore(TStore value);
}

/// <summary>An enum stored as its name (the repository's <c>HasConversion&lt;string&gt;()</c>).</summary>
public sealed class SqlEnumName<TEnum> : ISqlConverter<TEnum, string>
    where TEnum : struct, Enum
{
    public static string ToStore(TEnum value) => value.ToString();

    public static TEnum FromStore(string value) => Enum.Parse<TEnum>(value);
}

/// <summary>A <see cref="DateTimeOffset" /> stored as its UTC ticks in a 64-bit column (the repository's <c>UtcTicksConverter</c>).</summary>
public sealed class SqlUtcTicks : ISqlConverter<DateTimeOffset, long>
{
    public static long ToStore(DateTimeOffset value) => value.UtcTicks;

    public static DateTimeOffset FromStore(long value) => new(value, TimeSpan.Zero);
}

/// <summary>
///     A <c>[ConcurrencyAware]</c> row's version (the shape's <c>ulong RowVersion</c>) as the bigint the databases compare it as: PostgreSQL's
///     <c>xmin</c> and SQL Server's <c>rowversion</c> cast to one, the BIGINT column elsewhere. The providers bind no unsigned 64-bit value.
/// </summary>
public sealed class SqlRowVersion : ISqlConverter<ulong, long>
{
    public static long ToStore(ulong value) => unchecked((long)value);

    public static ulong FromStore(long value) => unchecked((ulong)value);
}

/// <summary>
///     A value stored as JSON text, through a System.Text.Json source-generated context (<typeparamref name="TContext" />
///     declares <typeparamref name="T" /> with <c>[JsonSerializable]</c>): no reflection, Native AOT clean. The column
///     is text; on PostgreSQL a <c>jsonb</c> column takes it with <c>[Column(Cast = "jsonb")]</c>.
/// </summary>
public sealed class SqlJson<T, TContext> : ISqlConverter<T, string>
    where TContext : JsonSerializerContext, new()
{
    private static readonly JsonTypeInfo<T> Info = (JsonTypeInfo<T>)(new TContext().GetTypeInfo(typeof(T)) ?? throw new InvalidOperationException($"{typeof(TContext).Name} does not declare {typeof(T).Name}: add [JsonSerializable(typeof({typeof(T).Name}))] to it."));

    public static string ToStore(T value) => JsonSerializer.Serialize(value, Info);

    public static T FromStore(string value) => JsonSerializer.Deserialize(value, Info) ?? throw new InvalidOperationException($"The JSON column held 'null' where a {typeof(T).Name} was expected.");
}

/// <summary>
///     A JSON column read as it is, without a type of its own (a payload, a document the application inspects): a <see cref="JsonDocument" />
///     parsed from the text and written back as its raw text, no serializer involved. The document is the caller's to dispose.
/// </summary>
public sealed class SqlJsonDocument : ISqlConverter<JsonDocument, string>
{
    public static string ToStore(JsonDocument value) => value.RootElement.GetRawText();

    public static JsonDocument FromStore(string value) => JsonDocument.Parse(value);
}
