using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Logging.Context;

/// <summary>
///     A snapshot of a <see cref="LogContext" />'s own properties: immutable, in the order they were first set,
///     read by key or by index.
/// </summary>
/// <remarks>
///     A context holds a handful of properties and is written a few times per request, then read on every log
///     call. A write publishes a new array one element longer (<see cref="With" />); a key is found by walking
///     it, faster than a hash at that size. A dictionary copied on every write cost four times the bytes. The
///     context keeps the array, and this view over it is built only when someone asks for the dictionary.
/// </remarks>
internal sealed class LogContextProperties(KeyValuePair<string, object?>[] items)
    : IReadOnlyDictionary<string, object?>, IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly KeyValuePair<string, object?>[] _items = items;

    /// <summary>The array this view reads, so the context can tell whether it is still the current one.</summary>
    public KeyValuePair<string, object?>[] Items => _items;

    public int Count => _items.Length;

    public KeyValuePair<string, object?> this[int index] => _items[index];

    public object? this[string key] => IndexOf(key) is var index and >= 0 ? _items[index].Value : throw new KeyNotFoundException(key);

    public IEnumerable<string> Keys => _items.Select(item => item.Key);

    public IEnumerable<object?> Values => _items.Select(item => item.Value);

    /// <summary>A copy of <paramref name="items" /> with <paramref name="key" /> set: replaced where it stands, or appended.</summary>
    public static KeyValuePair<string, object?>[] With(KeyValuePair<string, object?>[] items, string key, object? value)
    {
        var index = IndexOf(items, key);
        KeyValuePair<string, object?>[] next;
        if (index >= 0)
        {
            next = (KeyValuePair<string, object?>[])items.Clone();
        }
        else
        {
            next = new KeyValuePair<string, object?>[items.Length + 1];
            items.CopyTo(next, 0);
            index = items.Length;
        }

        next[index] = new KeyValuePair<string, object?>(key, value);
        return next;
    }

    /// <summary>
    ///     The pairs as one array, a repeated key keeping its first place and its last value, as setting them one by
    ///     one with <see cref="With" /> would.
    /// </summary>
    /// <remarks>
    ///     One array when the count is known, which it is for the list the request middleware passes; setting them
    ///     one by one copied into a new array per pair.
    /// </remarks>
    public static KeyValuePair<string, object?>[] From(IEnumerable<KeyValuePair<string, object?>> pairs)
    {
        if (pairs is not ICollection<KeyValuePair<string, object?>> collection)
        {
            KeyValuePair<string, object?>[] items = [];
            foreach (var pair in pairs)
                items = With(items, pair.Key, pair.Value);
            return items;
        }

        if (collection.Count == 0)
            return [];

        var buffer = new KeyValuePair<string, object?>[collection.Count];
        collection.CopyTo(buffer, 0);

        var length = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            var pair = buffer[i];
            var index = IndexOf(buffer, length, pair.Key);
            if (index >= 0)
                buffer[index] = new KeyValuePair<string, object?>(buffer[index].Key, pair.Value);
            else
                buffer[length++] = pair;
        }

        if (length < buffer.Length)
            Array.Resize(ref buffer, length);
        return buffer;
    }

    /// <summary>Whether a key appears twice in <paramref name="items" />: then <see cref="From" /> has to fold them.</summary>
    public static bool HasRepeatedKey(KeyValuePair<string, object?>[] items)
    {
        for (var i = 1; i < items.Length; i++)
        {
            if (IndexOf(items, i, items[i].Key) >= 0)
                return true;
        }

        return false;
    }

    /// <summary>A copy of <paramref name="items" /> without <paramref name="key" />, or the same array when it is not there.</summary>
    public static KeyValuePair<string, object?>[] Without(KeyValuePair<string, object?>[] items, string key)
    {
        var index = IndexOf(items, key);
        if (index < 0)
            return items;

        var next = new KeyValuePair<string, object?>[items.Length - 1];
        Array.Copy(items, 0, next, 0, index);
        Array.Copy(items, index + 1, next, index, items.Length - index - 1);
        return next;
    }

    /// <summary>Where <paramref name="key" /> stands in <paramref name="items" />, or -1.</summary>
    public static int IndexOf(KeyValuePair<string, object?>[] items, string key) => IndexOf(items, items.Length, key);

    private static int IndexOf(KeyValuePair<string, object?>[] items, int length, string key)
    {
        for (var i = 0; i < length; i++)
        {
            if (string.Equals(items[i].Key, key, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    public bool ContainsKey(string key) => IndexOf(key) >= 0;

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value)
    {
        var index = IndexOf(key);
        value = index >= 0 ? _items[index].Value : null;
        return index >= 0;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int IndexOf(string key) => IndexOf(_items, key);
}
