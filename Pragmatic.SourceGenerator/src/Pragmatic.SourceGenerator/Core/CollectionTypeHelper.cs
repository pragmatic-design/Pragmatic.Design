using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Recognises a collection by its <em>shape</em> — "implements <c>IEnumerable&lt;T&gt;</c>" — instead of by a
///     closed whitelist of BCL type names.
/// </summary>
/// <remarks>
///     A whitelist silently misclassifies everything it does not list: <c>HashSet&lt;T&gt;</c>,
///     <c>Dictionary&lt;TKey,TValue&gt;</c>, <c>ImmutableArray&lt;T&gt;</c>, <c>T[]</c>, <c>IReadOnlySet&lt;T&gt;</c>
///     and any user-defined collection. Consumers of that verdict (soft-delete cascade, DTO property
///     extraction) then treat a collection as a scalar and produce wrong output with no diagnostic.
///     Shape detection has no such blind spot.
///     <para>
///         <c>string</c> is excluded on purpose: it implements <c>IEnumerable&lt;char&gt;</c> but is always
///         modelled as a scalar.
///     </para>
/// </remarks>
internal static class CollectionTypeHelper
{
    /// <summary>
    ///     True when the type is a collection of elements (see <see cref="GetElementTypes" />).
    /// </summary>
    public static bool IsCollection(ITypeSymbol type) => GetElementTypes(type).Count > 0;

    /// <summary>
    ///     Returns the element types the collection yields, or an empty list when the type is not a
    ///     collection.
    /// </summary>
    /// <remarks>
    ///     More than one entry is returned only for the genuinely ambiguous case of a type implementing
    ///     <c>IEnumerable&lt;A&gt;</c> and <c>IEnumerable&lt;B&gt;</c>; callers decide what that means rather
    ///     than having a guess baked in here. A dictionary yields its <em>value</em> type as well as the
    ///     <c>KeyValuePair&lt;,&gt;</c> it formally enumerates, because a
    ///     <c>Dictionary&lt;TKey,TEntity&gt;</c> navigation is a collection of <c>TEntity</c> to every caller
    ///     that matters.
    /// </remarks>
    public static IReadOnlyList<ITypeSymbol> GetElementTypes(ITypeSymbol type)
    {
        // string is IEnumerable<char> but is never a collection in this codebase's sense.
        if (type.SpecialType == SpecialType.System_String)
            return [];

        if (type is IArrayTypeSymbol array)
            return [array.ElementType];

        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
            return [];

        if (type is not INamedTypeSymbol named)
            return [];

        var results = new List<ITypeSymbol>();

        // The declared type may itself be the IEnumerable<T> interface (AllInterfaces excludes self).
        AddElement(named, results);

        foreach (var iface in named.AllInterfaces)
            AddElement(iface, results);

        return results;
    }

    private static void AddElement(INamedTypeSymbol candidate, List<ITypeSymbol> results)
    {
        if (candidate.OriginalDefinition.SpecialType != SpecialType.System_Collections_Generic_IEnumerable_T)
            return;
        if (candidate.TypeArguments.Length != 1)
            return;

        Add(candidate.TypeArguments[0], results);

        // Dictionary<TKey,TValue> enumerates KeyValuePair<TKey,TValue>; the interesting element for
        // navigation/DTO purposes is TValue, so surface both.
        if (candidate.TypeArguments[0] is INamedTypeSymbol { TypeArguments.Length: 2 } pair &&
            pair.OriginalDefinition.MetadataName == "KeyValuePair`2" &&
            pair.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
            Add(pair.TypeArguments[1], results);
    }

    private static void Add(ITypeSymbol element, List<ITypeSymbol> results)
    {
        foreach (var existing in results)
        {
            if (SymbolEqualityComparer.Default.Equals(existing, element))
                return;
        }
        results.Add(element);
    }
}
