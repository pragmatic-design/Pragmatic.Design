using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     What shape a <c>[Query]</c> answers in, and which boundary's context it reads from.
/// </summary>
/// <remarks>
///     <para>
///         Read here rather than in each transform that needs it. Two consumers ask: the endpoint
///         transform, which turns the shape into a response type, and the query transform, which turns
///         it into a generated invoker. A second copy of "does this query page" would drift, and the
///         drift would be silent — a list answered where a page was published, or an invoker of the
///         wrong shape.
///     </para>
///     <para>
///         ⚠️ <b>Single is declared, never inferred.</b> "This filter happens to match one row" is not
///         something the generator can know, and guessing would turn a list into a 404.
///     </para>
/// </remarks>
internal static class QueryShapeReader
{
    /// <summary>The boundary whose keyed context the query reads, when the entity names one.</summary>
    public static string? BoundaryOf(ITypeSymbol entityType)
        => entityType is INamedTypeSymbol named ? BoundaryOwnershipReader.QualifiedBoundaryOf(named) : null;

    /// <summary>
    ///     Whether the query pages, said either way: by carrying the two properties, or by asking for
    ///     them with <c>Paged = true</c> and letting the generator write them.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Both halves, in one reader. The generated properties are written by this same
    ///     generator, so no transform can see them on the symbol: reading the symbol alone answers
    ///     "not paged" for a query that will implement <c>IPagedQuery</c>, and the endpoint then
    ///     publishes a list where the invoker returns a page.
    /// </remarks>
    public static bool IsPaged(INamedTypeSymbol symbol, AttributeData attribute)
        => PagingIsRequested(attribute) || DeclaresPaging(symbol);

    /// <summary>
    ///     Whether the author asked the generator for the paging surface, with <c>Paged = true</c>.
    /// </summary>
    public static bool PagingIsRequested(AttributeData attribute)
        => attribute.NamedArguments.Any(a => a.Key == "Paged" && a.Value.Value is true);

    /// <summary>
    ///     Whether the query carries <c>Page</c> and <c>PageSize</c> of its own, written by the author.
    /// </summary>
    public static bool DeclaresPaging(INamedTypeSymbol symbol)
    {
        var current = symbol;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { IsStatic: false } prop
                    && prop.Name is "Page" or "PageSize"
                    && prop.Type.SpecialType == SpecialType.System_Int32)
                    return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    ///     Whether the query asks for one row, which it says with <c>Single = true</c> on the attribute.
    /// </summary>
    /// <remarks>
    ///     Single wins over paging: a query that declares both is asking for one row, and its paging
    ///     properties are filters the author left behind.
    /// </remarks>
    public static bool IsSingle(AttributeData attribute)
        => attribute.NamedArguments.Any(a => a.Key == "Single" && a.Value.Value is true);

    /// <summary>
    ///     Whether the query maps its result type in memory, which it says with
    ///     <c>MapInMemory = true</c>.
    /// </summary>
    public static bool MapsInMemory(AttributeData attribute)
        => attribute.NamedArguments.Any(a => a.Key == "MapInMemory" && a.Value.Value is true);
}
