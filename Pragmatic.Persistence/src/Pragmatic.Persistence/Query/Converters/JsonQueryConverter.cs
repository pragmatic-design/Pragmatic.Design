using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace Pragmatic.Persistence.Query.Converters;

/// <summary>
///     TypeConverter that deserializes a JSON string into <typeparamref name="T" />.
///     Applied by the source generator on <c>[FilterDto]</c> classes used as <c>[ComplexFilter]</c> query
///     properties, enabling ASP.NET Core model binding from a URL query string.
/// </summary>
/// <remarks>
///     HTTP usage: <c>?Location={"cityGroup":{"city":"Rome"}}&amp;Page=1&amp;PageSize=20</c>
/// </remarks>
/// <typeparam name="T">The FilterDto type to deserialize.</typeparam>
public sealed class JsonQueryConverter<T> : TypeConverter
{
    // Input arrives from an untrusted URL query string. Cap depth and size to avoid
    // stack-overflow / DoS from deeply nested or oversized payloads.
    private const int MaxInputLength = 16 * 1024;

    // Web defaults: camelCase property names, case-insensitive matching
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string str && !string.IsNullOrWhiteSpace(str))
        {
            if (str.Length > MaxInputLength)
                throw new ArgumentException(
                    $"Query filter JSON exceeds the maximum allowed length of {MaxInputLength} characters.",
                    nameof(value));

            return JsonSerializer.Deserialize<T>(str, Options);
        }

        return base.ConvertFrom(context, culture, value);
    }
}
