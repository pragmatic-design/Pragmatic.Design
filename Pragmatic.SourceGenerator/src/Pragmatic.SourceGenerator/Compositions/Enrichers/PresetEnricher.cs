using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Compositions.Models;

namespace Pragmatic.SourceGenerator.Compositions.Enrichers;

/// <summary>
///     Detects [HasPresets] + [PresetProvider&lt;T&gt;] attributes on entity types
///     and produces a <see cref="PresetContribution"/>.
///     Only relevant for Create mutations.
/// </summary>
internal static class PresetEnricher
{
    private const string HasPresetsAttributeFqn = "Pragmatic.Persistence.Entity.HasPresetsAttribute";
    private const string PresetProviderPrefix = "Pragmatic.Persistence.Entity.PresetProviderAttribute<";

    public static PresetContribution? Enrich(INamedTypeSymbol entityType, bool isCreate)
    {
        if (!isCreate)
            return null;

        // Check for [HasPresets] marker
        var hasPresets = false;
        foreach (var attr in entityType.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == HasPresetsAttributeFqn)
            {
                hasPresets = true;
                break;
            }
        }

        if (!hasPresets)
            return null;

        // Collect [PresetProvider<T>] attributes
        var providers = ImmutableArray.CreateBuilder<PresetProviderModel>();

        foreach (var attr in entityType.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass?.OriginalDefinition is null)
                continue;

            var originalDef = attrClass.OriginalDefinition.ToDisplayString();
            if (!originalDef.StartsWith(PresetProviderPrefix))
                continue;

            if (attrClass is not INamedTypeSymbol { TypeArguments.Length: 1 } namedType)
                continue;

            var providerType = namedType.TypeArguments[0];
            var order = 0;

            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg is { Key: "Order", Value.Value: int o })
                    order = o;
            }

            providers.Add(new PresetProviderModel
            {
                ProviderTypeFqn = providerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Order = order
            });
        }

        if (providers.Count == 0)
            return null;

        // Sort by Order
        var sorted = providers.OrderBy(p => p.Order).ToImmutableArray();
        return new PresetContribution { Providers = sorted };
    }
}
