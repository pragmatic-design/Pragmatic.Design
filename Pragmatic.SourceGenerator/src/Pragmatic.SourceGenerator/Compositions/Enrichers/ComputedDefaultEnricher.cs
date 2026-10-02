using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Compositions.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Compositions.Enrichers;

/// <summary>
///     Detects [ComputedDefault&lt;TEntity, TValue, TGenerator&gt;] attributes on entity properties
///     and produces a <see cref="ComputedDefaultContribution"/>.
///     Only relevant for Create mutations.
/// </summary>
internal static class ComputedDefaultEnricher
{
    private const string AttributePrefix = "Pragmatic.Persistence.Entity.ComputedDefaultAttribute<";

    public static ComputedDefaultContribution? Enrich(INamedTypeSymbol entityType, bool isCreate)
    {
        if (!isCreate)
            return null;

        var properties = ImmutableArray.CreateBuilder<ComputedDefaultPropertyModel>();
        var entityFqn = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        foreach (var member in entityType.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;

            foreach (var attr in prop.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if (attrClass?.OriginalDefinition is null)
                    continue;

                var originalDef = attrClass.OriginalDefinition.ToDisplayString();

                // [GeneratedValue] is deliberately absent here. Wired through this create-time
                // machinery, an entity created by anything other than a create mutation would reach
                // the database with the column empty. GeneratedValueInterceptor fills it at
                // SaveChanges, where every create path converges — and doing both would be two
                // mechanisms for one guarantee.
                if (originalDef == "Pragmatic.Persistence.Entity.GeneratedValueAttribute")
                    continue;

                if (!originalDef.StartsWith("Pragmatic.Persistence.Entity.ComputedDefaultAttribute<"))
                    continue;

                if (attrClass is not INamedTypeSymbol { TypeArguments.Length: 3 } namedType)
                    continue;

                var valueType = namedType.TypeArguments[1];
                var generatorType = namedType.TypeArguments[2];

                // Determine setter: direct set if public, or SetXxx method for private setter
                var setterName = prop.SetMethod?.DeclaredAccessibility == Accessibility.Public
                    ? prop.Name
                    : $"Set{prop.Name}";

                properties.Add(new ComputedDefaultPropertyModel
                {
                    PropertyName = prop.Name,
                    SetterName = setterName,
                    ValueTypeFqn = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    GeneratorTypeFqn = generatorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    EntityTypeFqn = entityFqn
                });
            }
        }

        if (properties.Count == 0)
            return null;

        return new ComputedDefaultContribution { Properties = properties.ToImmutable() };
    }

}
