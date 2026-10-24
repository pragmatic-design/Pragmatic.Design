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
    /// <summary>The parent a trait's child names (<c>[TraitOf&lt;TParent&gt;]</c>, read by name); null for any other entity.</summary>
    public static INamedTypeSymbol? TraitParent(INamedTypeSymbol entity)
        => entity.GetAttributes().Select(a => a.AttributeClass).FirstOrDefault(c => c is { Name: "TraitOfAttribute", TypeArguments.Length: 1 })?.TypeArguments[0] as INamedTypeSymbol;

    /// <summary>A <c>[TemporalRelation]</c> entity (its validity, <c>ITemporalRelation</c>'s ValidFrom and ValidTo, its own).</summary>
    public static bool IsTemporal(INamedTypeSymbol entity) => PragmaticShape.Has(entity, "TemporalRelationAttribute");
}
