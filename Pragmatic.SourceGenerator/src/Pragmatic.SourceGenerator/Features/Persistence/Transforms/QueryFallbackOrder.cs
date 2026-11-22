using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The order a paged query falls back to when it declares none: the entity's mapped key (#938).
/// </summary>
internal static class QueryFallbackOrder
{
    /// <summary>
    ///     The key member to order by, or null when the query is not paged or orders elsewhere.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not for a query that pages through a canonical grid request, whose bridge applies the caller's sorts
    ///         (an <c>OrderBy</c> after it would replace them), nor for one over a base query or an aggregate view,
    ///         whose order is decided by code this transform does not see.
    ///     </para>
    ///     <para>
    ///         <c>PersistenceId</c> when the entity has it or <c>Id</c> is the generated alias of it, which is
    ///         unmapped; a hand-declared <c>Id</c> otherwise; null for a type with neither.
    ///     </para>
    /// </remarks>
    public static string? KeyFor(
        INamedTypeSymbol entity, bool isPaged, bool hasGridRequests, bool hasBaseQuery, bool resultIsAggregateView)
    {
        if (!isPaged || hasGridRequests || hasBaseQuery || resultIsAggregateView)
            return null;

        var declaresId = false;
        for (var type = entity; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IPropertySymbol { Name: "PersistenceId" })
                    return "PersistenceId";
                if (member is IPropertySymbol { Name: "Id" })
                    declaresId = true;
            }
        }

        if (QueryTransform.IdIsTheGeneratedAlias(entity))
            return "PersistenceId";
        return declaresId ? "Id" : null;
    }
}
