using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Persistence.Sql.Conversion;

/// <summary>An enum stored as its name (the repository's <c>HasConversion&lt;string&gt;()</c>).</summary>
public sealed class SqlEnumName<TEnum> : ISqlConverter<TEnum, string>
    where TEnum : struct, Enum
{
    public static string ToStore(TEnum value) => value.ToString();

    public static TEnum FromStore(string value) => Enum.Parse<TEnum>(value);
}
