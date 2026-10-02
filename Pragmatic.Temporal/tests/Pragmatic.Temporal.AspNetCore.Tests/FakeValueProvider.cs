using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;

namespace Pragmatic.Temporal.AspNetCore.Tests;

/// <summary>Minimal value provider for model-binder tests.</summary>
internal sealed class FakeValueProvider : IValueProvider
{
    private readonly Dictionary<string, StringValues> _values = new(StringComparer.OrdinalIgnoreCase);

    public FakeValueProvider With(string key, string? value)
    {
        _values[key] = value;
        return this;
    }

    public bool ContainsPrefix(string prefix)
    {
        return _values.ContainsKey(prefix);
    }

    public ValueProviderResult GetValue(string key)
    {
        return _values.TryGetValue(key, out var values)
            ? new ValueProviderResult(values)
            : ValueProviderResult.None;
    }
}
