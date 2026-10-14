using System.Collections.Frozen;

namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// Reads the properties of one type through getters the source generator wrote for it.
/// </summary>
internal sealed class GeneratedPropertyAccessor<T>(IReadOnlyDictionary<string, Func<T, object?>> getters)
    : IPropertyAccessor
{
    private readonly FrozenDictionary<string, Func<T, object?>> _getters =
        getters.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public object? GetValue(object target, string propertyName)
        => _getters.TryGetValue(propertyName, out var getter) ? getter((T)target) : null;

    public bool HasProperty(object target, string propertyName) => _getters.ContainsKey(propertyName);
}
