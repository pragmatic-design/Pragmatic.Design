using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     What the Sql engine reads off a Pragmatic entity to translate its filter families itself. The filter classes callers name
///     (<c>Disable&lt;Reservation.OwnershipFilter&gt;()</c>, <c>[WithoutFilter&lt;T&gt;]</c>) are Persistence's shape, the same types on
///     every engine; the engine does not evaluate them.
/// </summary>
internal static class PragmaticFilters
{
    /// <summary>
    ///     The parent a trait's child names (<c>[TraitOf&lt;TParent&gt;]</c>, which TraitFeature emits on the children it generates);
    ///     null for any other entity.
    /// </summary>
    public static INamedTypeSymbol? TraitParent(INamedTypeSymbol entity)
        => entity.GetAttributes().Select(a => a.AttributeClass)
            .FirstOrDefault(c => c is { Name: "TraitOfAttribute", TypeArguments.Length: 1 } && c.ContainingNamespace.ToDisplayString() == "Pragmatic.Persistence.Entity")
            ?.TypeArguments[0] as INamedTypeSymbol;

    /// <summary>
    ///     The bypass permission of a <c>[VisibleWhen]</c> rule that is also a permission-based filter (<c>IPermissionBasedFilter</c>, its
    ///     <c>BypassPermission</c> a string literal): the rule is the permission family's, lifted as EF Core lifts such a filter (Admin, the
    ///     caller's lifts, the bypass permission), not by its name only. TraitFeature declares one on a comment an engine persists
    ///     (<c>InternalVisibilityFilter</c>). Null for a plain rule.
    /// </summary>
    public static string? PermissionBypass(INamedTypeSymbol rule)
    {
        if (!rule.AllInterfaces.Any(i => i.ToDisplayString() == "Pragmatic.Persistence.Query.Filters.IPermissionBasedFilter"))
            return null;
        foreach (var reference in rule.GetMembers("BypassPermission").SelectMany(m => m.DeclaringSyntaxReferences))
        {
            if (reference.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax
                {
                    ExpressionBody.Expression: Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax literal,
                } && literal.Token.Value is string bypass)
                return bypass;
        }

        return null;
    }

    /// <summary>A <c>[TemporalRelation]</c> entity (its validity, <c>ITemporalRelation</c>'s ValidFrom and ValidTo, its own).</summary>
    public static bool IsTemporal(INamedTypeSymbol entity) => PragmaticShape.Has(entity, "TemporalRelationAttribute");
}
