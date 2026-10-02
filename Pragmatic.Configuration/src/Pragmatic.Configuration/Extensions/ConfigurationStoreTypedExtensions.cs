using System.Globalization;

namespace Pragmatic.Configuration;

/// <summary>
///     Typed convenience over the string-based <see cref="IConfigurationStore" />: read and write common
///     scalar types (string, bool, the numeric types, <see cref="Guid" />, enums) without hand-rolling
///     conversion. Values round-trip through the invariant culture, so they are stable across environments.
///     Unsupported types throw <see cref="NotSupportedException" /> — bind a POCO via the options pipeline for
///     anything richer.
/// </summary>
public static class ConfigurationStoreTypedExtensions
{
    /// <summary>Reads and converts a value, or <paramref name="defaultValue" /> when the key is unset.</summary>
    public static async Task<T> GetValueAsync<T>(
        this IConfigurationStore store, string key, T defaultValue = default!, CancellationToken ct = default)
    {
        var raw = await store.GetAsync(key, ct).ConfigureAwait(false);
        return raw is null ? defaultValue : Parse<T>(raw);
    }

    /// <summary>Reads and converts a tenant-scoped value, or <paramref name="defaultValue" /> when unset.</summary>
    public static async Task<T> GetValueAsync<T>(
        this IConfigurationStore store, string key, string tenantId, T defaultValue = default!, CancellationToken ct = default)
    {
        var raw = await store.GetAsync(key, tenantId, ct).ConfigureAwait(false);
        return raw is null ? defaultValue : Parse<T>(raw);
    }

    /// <summary>Converts <paramref name="value" /> to its invariant string form and stores it.</summary>
    public static Task SetValueAsync<T>(
        this IConfigurationStore store, string key, T value, string? tenantId = null, CancellationToken ct = default)
        => store.SetAsync(key, Format(value), tenantId, ct);

    private static string Format<T>(T value) => value switch
    {
        null => throw new ArgumentNullException(nameof(value)),
        string s => s,
        bool b => b ? "true" : "false",
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? throw new NotSupportedException($"Cannot convert {typeof(T)} to a configuration string.")
    };

    private static T Parse<T>(string raw)
    {
        var target = typeof(T);
        var underlying = Nullable.GetUnderlyingType(target) ?? target;

        object result =
            underlying == typeof(string) ? raw
            : underlying == typeof(bool) ? bool.Parse(raw)
            : underlying == typeof(int) ? int.Parse(raw, CultureInfo.InvariantCulture)
            : underlying == typeof(long) ? long.Parse(raw, CultureInfo.InvariantCulture)
            : underlying == typeof(double) ? double.Parse(raw, CultureInfo.InvariantCulture)
            : underlying == typeof(decimal) ? decimal.Parse(raw, CultureInfo.InvariantCulture)
            : underlying == typeof(Guid) ? Guid.Parse(raw)
            : underlying == typeof(TimeSpan) ? TimeSpan.Parse(raw, CultureInfo.InvariantCulture)
            : underlying == typeof(DateTimeOffset) ? DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture)
            : underlying.IsEnum ? Enum.Parse(underlying, raw, ignoreCase: true)
            : throw new NotSupportedException(
                $"Cannot convert a configuration string to {target}. Bind a POCO via the options pipeline instead.");

        return (T)result;
    }
}
