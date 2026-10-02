using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Helper methods for resolving property paths in mappings.
/// </summary>
internal static class PropertyPathResolver
{
    /// <summary>
    ///     Resolves a dot-separated property path to an expression.
    /// </summary>
    /// <summary>
    ///     The type at the end of a path, as a symbol, or <c>null</c> when the path does not resolve to
    ///     an ordinary property of an ordinary type.
    /// </summary>
    /// <remarks>
    ///     <see cref="ResolvePropertyPath" /> answers with a display string, which is enough to render an
    ///     expression and not enough to ask a type what its members are. This walks the same segments
    ///     for the callers that need the symbol — rather than each of them walking the path again, which
    ///     is how the two readings of one path come apart.
    ///     <para>
    ///         ⚠️ Trait-generated properties stop it: they are known by name and type <em>string</em>,
    ///         not by symbol. A caller gets <c>null</c> and keeps whatever it did before.
    ///     </para>
    /// </remarks>
    public static ITypeSymbol? ResolvePropertyType(string path, INamedTypeSymbol sourceSymbol)
    {
        ITypeSymbol? current = sourceSymbol;

        foreach (var part in path.Split('.'))
        {
            if (current is not INamedTypeSymbol named)
                return null;

            var prop = PropertyAnalyzer.GetAllProperties(named)
                .FirstOrDefault(p => string.Equals(p.Name, part, StringComparison.OrdinalIgnoreCase));

            if (prop is null)
                return null;

            current = prop.Type;
        }

        return ReferenceEquals(current, sourceSymbol) ? null : current;
    }

    public static (string? expr, string? type, bool nullable) ResolvePropertyPath(
        string path,
        INamedTypeSymbol sourceSymbol)
    {
        var parts = path.Split('.');
        ITypeSymbol currentType = sourceSymbol;
        var exprParts = new List<string> { "entity" };
        var nullable = false;

        foreach (var part in parts)
        {
            if (currentType is not INamedTypeSymbol namedType)
                return (null, null, false);

            var prop = PropertyAnalyzer.GetAllProperties(namedType)
                .FirstOrDefault(p => string.Equals(p.Name, part, StringComparison.OrdinalIgnoreCase));

            if (prop is null)
            {
                // Fallback: check trait-generated properties (e.g., Id, CreatedAt from [Entity]/[Auditable])
                var traitProp = TryResolveTraitProperty(namedType, part);
                if (traitProp is null)
                    return (null, null, false);

                // A generated reference navigation is not terminal: the path continues through it.
                // `WorkItem.Description` has to walk into WorkItem, which [Relation.ManyToOne<WorkItem>]
                // will create — stopping here is what made every flattening over a generated relation
                // fail on its first segment. A collection cannot be walked into, so it still stops.
                if (traitProp.Value is { TypeSymbol: not null, IsCollection: false })
                {
                    exprParts.Add(traitProp.Value.Name);
                    currentType = traitProp.Value.TypeSymbol;
                    continue;
                }

                exprParts.Add(traitProp.Value.Name);
                // Trait properties are terminal — can't navigate further through them
                var isNullable = traitProp.Value.TypeFullName.EndsWith("?");
                if (isNullable) nullable = true;

                // Build final expression for remaining parts (should be last in path)
                var finalExpr = string.Join(".", exprParts);
                if (nullable && exprParts.Count > 2)
                    finalExpr = exprParts[0] + "." + string.Join("?.", exprParts.Skip(1));

                var cleanType = traitProp.Value.TypeFullName.TrimEnd('?');
                return (finalExpr, cleanType, nullable);
            }

            exprParts.Add(prop.Name);
            currentType = prop.Type;

            if (prop.Type.NullableAnnotation == NullableAnnotation.Annotated)
                nullable = true;
        }

        var expr = string.Join(".", exprParts);
        // Insert null-conditional for nullable navigation
        if (nullable && exprParts.Count > 2)
            expr = exprParts[0] + "." + string.Join("?.", exprParts.Skip(1));

        return (expr, currentType.ToDisplayString(), nullable);
    }

    /// <summary>
    ///     Tries to flatten a property name to a nested property path.
    ///     E.g., AddressCity -> Address.City
    /// </summary>
    public static (bool success, string? expr, string? type, bool nullable, bool isEnum) TryFlatten(
        string propertyName,
        INamedTypeSymbol sourceSymbol)
    {
        // Try to find nested property: AddressCity -> Address.City
        foreach (var sourceProp in PropertyAnalyzer.GetAllProperties(sourceSymbol))
        {
            if (sourceProp.Type is not INamedTypeSymbol nestedType)
                continue;

            if (!propertyName.StartsWith(sourceProp.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = propertyName.Substring(sourceProp.Name.Length);
            if (string.IsNullOrEmpty(remainder))
                continue;

            var nestedProp = PropertyAnalyzer.GetAllProperties(nestedType)
                .FirstOrDefault(p => string.Equals(p.Name, remainder, StringComparison.OrdinalIgnoreCase));

            if (nestedProp is not null)
            {
                // Where the value lives decides whether the guard means anything. A value object (and
                // Money) sits in the owner's own row: EF materialises it with the entity, so a
                // non-nullable one is never null, and `?.` defended against a state the model does not
                // have while handing a `string?` to the DTO's `string` — one CS8601 per flattened
                // member, and a build failure wherever warnings are errors, which is every Pragmatic
                // project (sixteen of them across two DTOs in the Invoicing example).
                // A navigation is another row: it can be left-joined away, or simply not Included, so
                // there the guard stays whatever the annotation claims.
                var readsThroughNull = sourceProp.Type.NullableAnnotation == NullableAnnotation.Annotated
                                       || !SqlTranslatableAnalyzer.IsProjectedWhole(sourceProp.Type);
                var access = readsThroughNull ? "?." : ".";

                // Read through `?.`, a value-type member comes back as a `Nullable<T>` whatever the
                // annotations say. Taken from the annotations alone, an `int` behind a non-nullable
                // navigation was not nullable, so neither mapping turned the `int?` back into an `int`
                // and neither compiled. Read through `.` it keeps its own type.
                var nullable = readsThroughNull
                    ? sourceProp.Type.NullableAnnotation == NullableAnnotation.Annotated
                      || nestedProp.Type.NullableAnnotation == NullableAnnotation.Annotated
                      || nestedProp.Type is { IsValueType: true } and not INamedTypeSymbol
                      {
                          OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                      }
                    : nestedProp.Type.NullableAnnotation == NullableAnnotation.Annotated;

                return (true, $"entity.{sourceProp.Name}{access}{nestedProp.Name}",
                    nestedProp.Type.ToDisplayString(), nullable, TypeConversionHelper.IsEnumType(nestedProp.Type));
            }
        }

        // A navigation declared with [Relation] is generated, so it is not among the properties above
        // during this pass — and it is the navigation this framework recommends. The explicit path
        // already asks the generators what the type will have, and the convention has to ask too.
        // A generated member with the very name wins: `CustomerId` is the foreign
        // key column, not a join to read `Customer.Id`.
        var generated = TraitPropertyResolver.GetGeneratedProperties(sourceSymbol);
        if (generated.Any(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase)))
            return (false, null, null, false, false);

        foreach (var navigation in generated)
        {
            if (navigation is not { TypeSymbol: INamedTypeSymbol nestedType, IsCollection: false })
                continue;

            if (!propertyName.StartsWith(navigation.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = propertyName.Substring(navigation.Name.Length);
            if (string.IsNullOrEmpty(remainder))
                continue;

            var nestedProp = PropertyAnalyzer.GetAllProperties(nestedType)
                .FirstOrDefault(p => string.Equals(p.Name, remainder, StringComparison.OrdinalIgnoreCase));

            // Nullable whatever the relation says: the path reads through `?.`, and the generated
            // navigation carries no annotation the reader could trust instead.
            if (nestedProp is not null)
                return (true, $"entity.{navigation.Name}?.{nestedProp.Name}",
                    nestedProp.Type.ToDisplayString(), true, TypeConversionHelper.IsEnumType(nestedProp.Type));
        }

        return (false, null, null, false, false);
    }

    /// <summary>
    ///     Tries to detect concatenation patterns.
    ///     E.g., FullName -> FirstName + " " + LastName
    /// </summary>
    public static (bool success, string? expr) TryConcatenation(
        string propertyName,
        ImmutableArray<IPropertySymbol> sourceProperties)
    {
        // Common patterns: FullName -> FirstName + LastName
        if (propertyName.Equals("FullName", StringComparison.OrdinalIgnoreCase))
        {
            var firstName = sourceProperties.FirstOrDefault(p =>
                p.Name.Equals("FirstName", StringComparison.OrdinalIgnoreCase));
            var lastName = sourceProperties.FirstOrDefault(p =>
                p.Name.Equals("LastName", StringComparison.OrdinalIgnoreCase));

            if (firstName is not null && lastName is not null)
                return (true, $"entity.{firstName.Name} + \" \" + entity.{lastName.Name}");
        }

        return (false, null);
    }

    /// <summary>
    ///     Attempts to resolve a property name from trait-generated properties on the given type.
    /// </summary>
    private static TraitPropertyResolver.VirtualProperty? TryResolveTraitProperty(
        INamedTypeSymbol namedType,
        string propertyName)
    {
        var traitProperties = TraitPropertyResolver.GetGeneratedProperties(namedType);
        foreach (var vp in traitProperties)
        {
            if (string.Equals(vp.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return vp;
        }

        return null;
    }
}
