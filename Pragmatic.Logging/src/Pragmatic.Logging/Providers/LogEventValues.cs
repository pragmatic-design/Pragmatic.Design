using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Logging.Providers;

/// <summary>
///     The properties or the scopes of a <see cref="LogEvent" />: an ordered list of key and value, reused from call
///     to call, read by index or by key.
/// </summary>
/// <remarks>
///     <para>
///         Setting a key that is already there replaces its value where it stands, as a dictionary does: a context
///         property with the name of a call's property overwrites it, and the order is the one the keys arrived in.
///         A call carries a handful of properties, so a key is found by walking the list, which is faster than a
///         hash at that size and allocates nothing once the list has grown to it.
///     </para>
///     <para>
///         ⚠️ Valid only while the provider writes the event: the next call on the thread reuses it. A provider
///         that keeps the event beyond the call keeps <see cref="LogEvent.ToEntry" /> instead.
///     </para>
/// </remarks>
public sealed class LogEventValues : IDictionary<string, object?>, IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly List<KeyValuePair<string, object?>> _items = [];

    /// <inheritdoc cref="ICollection{T}.Count" />
    public int Count => _items.Count;

    /// <summary>The value at <paramref name="index" />, in the order the keys arrived.</summary>
    public KeyValuePair<string, object?> this[int index] => _items[index];

    /// <inheritdoc />
    public object? this[string key]
    {
        get => IndexOf(key) is var index and >= 0 ? _items[index].Value : throw new KeyNotFoundException(key);
        set
        {
            var index = IndexOf(key);
            if (index >= 0)
                _items[index] = new KeyValuePair<string, object?>(key, value);
            else
                _items.Add(new KeyValuePair<string, object?>(key, value));
        }
    }

    /// <summary>Appends a pair without looking for its key: for scopes, which may repeat one.</summary>
    public void Append(string key, object? value) => _items.Add(new KeyValuePair<string, object?>(key, value));

    /// <summary>The pairs in order, without allocating an enumerator.</summary>
    public List<KeyValuePair<string, object?>>.Enumerator GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value)
    {
        var index = IndexOf(key);
        value = index >= 0 ? _items[index].Value : null;
        return index >= 0;
    }

    /// <inheritdoc />
    public bool ContainsKey(string key) => IndexOf(key) >= 0;

    /// <inheritdoc />
    public void Add(string key, object? value)
    {
        if (IndexOf(key) >= 0)
            throw new ArgumentException($"An item with the key '{key}' has already been added.", nameof(key));
        _items.Add(new KeyValuePair<string, object?>(key, value));
    }

    /// <inheritdoc />
    public bool Remove(string key)
    {
        var index = IndexOf(key);
        if (index < 0)
            return false;

        _items.RemoveAt(index);
        return true;
    }

    /// <inheritdoc />
    public void Clear() => _items.Clear();

    /// <inheritdoc />
    public ICollection<string> Keys => _items.ConvertAll(p => p.Key);

    /// <inheritdoc />
    public ICollection<object?> Values => _items.ConvertAll(p => p.Value);

    bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;

    void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item) => Add(item.Key, item.Value);

    bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item)
        => IndexOf(item.Key) is var index and >= 0 && Equals(_items[index].Value, item.Value);

    void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
        => _items.CopyTo(array, arrayIndex);

    bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item)
        => ((ICollection<KeyValuePair<string, object?>>)this).Contains(item) && Remove(item.Key);

    IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    private int IndexOf(string key)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].Key, key, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
