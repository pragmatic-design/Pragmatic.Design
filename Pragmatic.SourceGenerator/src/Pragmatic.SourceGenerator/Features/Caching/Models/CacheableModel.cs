using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Caching.Models;

internal sealed record CacheableModel : GeneratorModel
{
    public required EquatableArray<CacheKeyPropertyModel> KeyProperties { get; init; }
    public required string Duration { get; init; }
    public EquatableArray<string> Tags { get; init; } = EquatableArray<string>.Empty;
    public bool Sliding { get; init; }
    public int Priority { get; init; }
    public bool IsPartial { get; init; }
    public int TotalPropertyCount { get; init; }
    public EquatableArray<string> AllPropertyNames { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Properties whose complex type the key walk could not read to the bottom, reported as
    ///     PRAG1705. Empty in the ordinary case.
    /// </summary>
    public EquatableArray<string> UnreadableKeyProperties { get; init; } = EquatableArray<string>.Empty;

    /// <summary>FQN of the Category type, or null for default.</summary>
    public string? CategoryTypeFqn { get; init; }

    /// <summary>Declaration position for diagnostics (excluded from equality — see LocationInfo).</summary>
    public LocationInfo? Location { get; init; }
}

internal sealed record CacheKeyPropertyModel
{
    public required string Name { get; init; }
    public required string KeyName { get; init; }
    public required string Type { get; init; }
    public int Order { get; init; }

    /// <summary>
    ///     True when the member is a collection (array or IEnumerable, excluding string). Collection
    ///     values must be serialized element-by-element in the cache key: <c>Convert.ToString</c> on a
    ///     collection yields the type name, not the contents, so two different lists would otherwise
    ///     collapse to the same key and serve stale/wrong cached data.
    /// </summary>
    public bool IsCollection { get; init; }

    /// <summary>
    ///     The fragments this property contributes to the key: itself for a scalar, one per nested
    ///     scalar for a complex object.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A complex object cannot be a fragment on its own. <c>Convert.ToString</c> on a class
    ///     returns the <b>type name</b> — the same string for every instance — so the fragment was a
    ///     constant and every value of the property collapsed onto one cache entry, serving one
    ///     caller's page to another. The same trap was already known for collections, which are
    ///     serialised element by element for exactly this reason.
    ///     <para>
    ///     <c>required</c>, and not merely defaulted: a property with no parts contributes
    ///     nothing to the key, so a producer that forgot this would emit a key that tells no two
    ///     requests apart — the same failure one level up. Better a compiler error than a default
    ///     that is wrong in silence.
    ///     </para>
    /// </remarks>
    public required EquatableArray<CacheKeyPartModel> Parts { get; init; }
}

/// <summary>One fragment of the key: a label and the accessor tail that reads its value.</summary>
/// <param name="Label">What the fragment is called in the key, e.g. <c>Location.City</c>.</param>
/// <param name="Tail">
///     What to append to the root to read it — <c>""</c> for the property itself, <c>"?.City"</c> for a
///     nested scalar. Null-conditional throughout, so a null anywhere along the path yields an empty
///     fragment rather than an exception.
/// </param>
/// <param name="IsCollection">Whether this fragment is a collection, and must be serialised element by element.</param>
/// <remarks>
///     A tail rather than a whole expression because the root differs between the two places the key is
///     built: the instance method reads <c>Location</c>, the static <c>Create</c> helper reads its
///     parameter <c>location</c>. One model, two roots — and if they ever disagreed the helper would
///     write entries the query never reads.
/// </remarks>
internal sealed record CacheKeyPartModel(string Label, string Tail, bool IsCollection);
