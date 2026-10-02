using System.Collections;

namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// Property accessor for <see cref="IDictionary{TKey,TValue}"/> and nested dictionaries.
/// Zero reflection — works with <c>Dictionary&lt;string, object?&gt;</c>.
/// </summary>
public sealed class DictionaryPropertyAccessor : IPropertyAccessor
{
    public static DictionaryPropertyAccessor Instance { get; } = new();

    public object? GetValue(object target, string propertyName)
    {
        if (target is IDictionary<string, object?> dict)
            return dict.TryGetValue(propertyName, out var value) ? value : null;

        if (target is IDictionary nonGenericDict)
            return nonGenericDict.Contains(propertyName) ? nonGenericDict[propertyName] : null;

        return null;
    }

    public bool HasProperty(object target, string propertyName)
    {
        if (target is IDictionary<string, object?> dict) return dict.ContainsKey(propertyName);
        if (target is IDictionary nonGenericDict) return nonGenericDict.Contains(propertyName);
        return false;
    }
}
