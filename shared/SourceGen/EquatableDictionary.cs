using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Value-equatable wrapper over <see cref="ImmutableDictionary{TKey, TValue}" /> for use in
///     cached incremental-generator model records.
/// </summary>
/// <remarks>
///     <see cref="ImmutableDictionary{TKey, TValue}" /> implements equality by reference, so a model
///     holding one is not value-equatable and silently defeats incremental caching (same problem as
///     a raw <see cref="ImmutableArray{T}" /> — see <see cref="EquatableArray{T}" />). Equality and
///     hashing are order-insensitive, matching dictionary semantics.
/// </remarks>
internal readonly struct EquatableDictionary<TKey, TValue> : IEquatable<EquatableDictionary<TKey, TValue>>,
    IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    /// <summary>An empty dictionary.</summary>
    public static readonly EquatableDictionary<TKey, TValue> Empty = new(ImmutableDictionary<TKey, TValue>.Empty);

    private readonly ImmutableDictionary<TKey, TValue>? _dictionary;

    /// <summary>Wraps an existing <see cref="ImmutableDictionary{TKey, TValue}" />.</summary>
    public EquatableDictionary(ImmutableDictionary<TKey, TValue> dictionary) => _dictionary = dictionary;

    private ImmutableDictionary<TKey, TValue> Inner => _dictionary ?? ImmutableDictionary<TKey, TValue>.Empty;

    /// <inheritdoc />
    public int Count => Inner.Count;

    /// <summary>True when the dictionary is default or empty.</summary>
    public bool IsEmpty => Inner.IsEmpty;

    /// <inheritdoc />
    public TValue this[TKey key] => Inner[key];

    /// <inheritdoc />
    public IEnumerable<TKey> Keys => Inner.Keys;

    /// <inheritdoc />
    public IEnumerable<TValue> Values => Inner.Values;

    /// <inheritdoc />
    public bool ContainsKey(TKey key) => Inner.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(TKey key, out TValue value) => Inner.TryGetValue(key, out value!);

    /// <summary>Returns a copy with the entry added (dictionary semantics of <see cref="ImmutableDictionary{TKey, TValue}.Add" />).</summary>
    public EquatableDictionary<TKey, TValue> Add(TKey key, TValue value) => new(Inner.Add(key, value));

    /// <summary>Returns the underlying <see cref="ImmutableDictionary{TKey, TValue}" /> (empty if default).</summary>
    public ImmutableDictionary<TKey, TValue> AsImmutableDictionary() => Inner;

    /// <summary>Implicitly wraps an <see cref="ImmutableDictionary{TKey, TValue}" /> (so transforms need no change).</summary>
    public static implicit operator EquatableDictionary<TKey, TValue>(ImmutableDictionary<TKey, TValue> dictionary)
        => new(dictionary);

    /// <inheritdoc />
    public bool Equals(EquatableDictionary<TKey, TValue> other)
    {
        var self = Inner;
        var theirs = other.Inner;

        if (ReferenceEquals(self, theirs))
            return true;

        if (self.Count != theirs.Count)
            return false;

        foreach (var kvp in self)
        {
            if (!theirs.TryGetValue(kvp.Key, out var otherValue) ||
                !EqualityComparer<TValue>.Default.Equals(kvp.Value, otherValue))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EquatableDictionary<TKey, TValue> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Order-insensitive: XOR per-entry hashes so enumeration order cannot change the result.
        var hash = 0;
        foreach (var kvp in Inner)
            hash ^= unchecked((kvp.Key.GetHashCode() * 397) ^ (kvp.Value?.GetHashCode() ?? 0));

        return hash;
    }

    public static bool operator ==(EquatableDictionary<TKey, TValue> left, EquatableDictionary<TKey, TValue> right)
        => left.Equals(right);

    public static bool operator !=(EquatableDictionary<TKey, TValue> left, EquatableDictionary<TKey, TValue> right)
        => !left.Equals(right);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => Inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
