using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Value-equatable wrapper over <see cref="ImmutableArray{T}" /> for use in cached
///     incremental-generator model records.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ImmutableArray{T}" /> implements equality by comparing the underlying array
///         <em>reference</em>, not its contents. A model <c>record</c> holding a raw
///         <see cref="ImmutableArray{T}" /> field is therefore not value-equatable, which silently
///         defeats incremental-generator caching: the pipeline stage re-runs on every keystroke
///         even when nothing relevant changed.
///     </para>
///     <para>
///         Use <c>EquatableArray&lt;T&gt;</c> for every collection field on a model that flows
///         through the generator pipeline. Transforms can keep producing
///         <see cref="ImmutableArray{T}" /> — the implicit conversion wraps it at assignment.
///     </para>
/// </remarks>
[System.Runtime.CompilerServices.CollectionBuilder(typeof(EquatableArray), nameof(EquatableArray.Create))]
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    /// <summary>An empty array.</summary>
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    private readonly ImmutableArray<T> _array;

    /// <summary>Wraps an existing <see cref="ImmutableArray{T}" />.</summary>
    public EquatableArray(ImmutableArray<T> array) => _array = array;

    /// <summary>Number of elements (0 for a default/uninitialized array).</summary>
    public int Count => _array.IsDefault ? 0 : _array.Length;

    /// <summary>Alias of <see cref="Count" />, mirroring <see cref="ImmutableArray{T}.Length" />.</summary>
    public int Length => Count;

    /// <summary>True when the array is default or empty.</summary>
    public bool IsDefaultOrEmpty => _array.IsDefaultOrEmpty;

    /// <inheritdoc />
    public T this[int index] => _array[index];

    /// <summary>Returns the underlying <see cref="ImmutableArray{T}" /> (empty if default).</summary>
    public ImmutableArray<T> AsImmutableArray() => _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    /// <summary>Implicitly wraps an <see cref="ImmutableArray{T}" /> (so transforms need no change).</summary>
    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);

    /// <inheritdoc />
    /// <remarks>
    ///     <c>default</c> and <see cref="Empty" /> are equal: both are "no elements", and every other
    ///     member here (<see cref="Count" />, the enumerator, <see cref="AsImmutableArray" />) already
    ///     normalises the default to empty. Distinguishing them only in equality would let two models
    ///     that are observably identical compare unequal — a silent cache miss in the incremental
    ///     pipeline, which is exactly what this type exists to prevent.
    ///     <see cref="EquatableDictionary{TKey,TValue}" /> normalises the same way.
    /// </remarks>
    public bool Equals(EquatableArray<T> other)
    {
        var self = AsImmutableArray();
        var theirs = other.AsImmutableArray();

        if (self.Length != theirs.Length)
            return false;

        for (var i = 0; i < self.Length; i++)
            if (!EqualityComparer<T>.Default.Equals(self[i], theirs[i]))
                return false;

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <inheritdoc />
    /// <remarks>Hashes the default array as empty, to stay consistent with <see cref="Equals(EquatableArray{T})" />.</remarks>
    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in AsImmutableArray())
            hash = unchecked((hash * 31) + (item?.GetHashCode() ?? 0));

        return hash;
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() =>
        (_array.IsDefault ? ImmutableArray<T>.Empty : _array).AsEnumerable().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Helpers for building <see cref="EquatableArray{T}" />.</summary>
internal static class EquatableArray
{
    /// <summary>Collection-expression builder (enables <c>[a, b, ..]</c> targeting <see cref="EquatableArray{T}" />).</summary>
    public static EquatableArray<T> Create<T>(ReadOnlySpan<T> values) => new(ImmutableArray.Create(values));

    /// <summary>Wraps an <see cref="ImmutableArray{T}" /> as an <see cref="EquatableArray{T}" />.</summary>
    public static EquatableArray<T> AsEquatableArray<T>(this ImmutableArray<T> array) => new(array);

    /// <summary>Materializes a sequence into an <see cref="EquatableArray{T}" />.</summary>
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> source) => new(source.ToImmutableArray());
}
