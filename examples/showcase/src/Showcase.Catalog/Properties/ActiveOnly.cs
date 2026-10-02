using System.Linq.Expressions;

namespace Showcase.Catalog.Properties;

/// <summary>
///     A deactivated property is not part of the catalogue: nothing lists it, nothing books it.
/// </summary>
/// <remarks>
///     <para>
///         This rule is one fact about the entity, so it is declared on the entity with
///         <c>[VisibleWhen&lt;ActiveOnly&gt;]</c> and applies to root queries, includes, joins and
///         subqueries alike. As a line repeated at each call site —
///         <c>.Where(PropertySpecifications.IsActive())</c> in the grid endpoint and again in the
///         DevExpress one — every read that did not remember it would show deactivated properties.
///     </para>
///     <para>
///         Seeing past it is <c>[WithoutFilter&lt;ActiveOnly&gt;]</c> on an operation that declares
///         the permission for it — <c>SearchDeactivatedPropertiesQuery</c> is the one that does.
///     </para>
/// </remarks>
public sealed class ActiveOnly : VisibilityRule<Property>
{
    /// <inheritdoc />
    public override Expression<Func<Property, bool>> ToExpression() => property => property.IsActive;
}
