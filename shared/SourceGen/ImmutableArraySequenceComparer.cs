using System.Collections.Generic;
using System.Collections.Immutable;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Element-wise equality for <see cref="ImmutableArray{T}"/>. The built-in
///     <c>ImmutableArray&lt;T&gt;.Equals</c> compares the backing-array REFERENCE, so two arrays with equal
///     elements but distinct backing arrays compare unequal — which makes an aggregate
///     <c>IncrementalValueProvider&lt;ImmutableArray&lt;T&gt;&gt;</c> re-run every downstream output on each
///     compilation change. Apply this via <c>.WithComparer(...)</c> so the step stays cached when the
///     content is unchanged. <typeparamref name="T"/> must itself be value-equatable.
/// </summary>
internal sealed class ImmutableArraySequenceComparer<T> : IEqualityComparer<ImmutableArray<T>>
{
    public static readonly ImmutableArraySequenceComparer<T> Instance = new();

    public bool Equals(ImmutableArray<T> x, ImmutableArray<T> y)
    {
        if (x.IsDefault)
            return y.IsDefault;
        if (y.IsDefault || x.Length != y.Length)
            return false;

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < x.Length; i++)
        {
            if (!comparer.Equals(x[i], y[i]))
                return false;
        }

        return true;
    }

    public int GetHashCode(ImmutableArray<T> obj)
    {
        if (obj.IsDefault)
            return 0;

        var comparer = EqualityComparer<T>.Default;
        var hash = 17;
        foreach (var item in obj)
            hash = (hash * 31) + (item is null ? 0 : comparer.GetHashCode(item));

        return hash;
    }
}
