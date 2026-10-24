using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Persistence.Sql.Conversion;

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
