// Pragmatic.SourceGenerator - Composition - Pipeline Step Transform

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Transforms [StartupStep] classes into StartupModel.
/// </summary>
internal static class StartupTransform
{
    private const string IStartupStepFullName = "Pragmatic.Composition.Abstractions.IStartupStep";

    /// <summary>
    ///     Transforms a class declaration into a StartupModel if valid.
    /// </summary>
    public static StartupModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        // Mis-declarations return an INVALID model (not null) so CompositionFeature can report a
        // diagnostic instead of silently dropping the step (which left it unregistered and never run).
        if (typeSymbol.TypeKind != TypeKind.Class)
            return CreateInvalid(typeSymbol, InvalidReason.NotClass);          // → PRAG1631

        if (!ImplementsStartupStep(typeSymbol))
            return CreateInvalid(typeSymbol, InvalidReason.NotStartupStep);    // → PRAG1630

        // Get order from the Order property
        var order = GetOrder(typeSymbol);

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var requiredConfigs = ReadRequiredConfigSections(typeSymbol);

        return new StartupModel
        {
            Namespace = ns,
            TypeName = typeSymbol.Name,
            FullTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Priority = order,
            Accessibility = typeSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            RequiredConfigSections = requiredConfigs,
            LocationInfo = LocationInfo.From(typeSymbol.Locations.FirstOrDefault())
        };
    }

    private static StartupModel CreateInvalid(INamedTypeSymbol symbol, InvalidReason reason) => new()
    {
        Namespace = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString(),
        TypeName = symbol.Name,
        FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        Priority = 0,
        Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
        LocationInfo = LocationInfo.From(symbol.Locations.FirstOrDefault()),
        InvalidReason = reason
    };

    private static ImmutableArray<string> ReadRequiredConfigSections(INamedTypeSymbol typeSymbol)
    {
        var sections = ImmutableArray.CreateBuilder<string>();

        foreach (var attr in typeSymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var fullName = attrClass.ToDisplayString();
            if (!fullName.EndsWith("RequiresConfigAttribute", StringComparison.Ordinal))
                continue;

            if (attr.ConstructorArguments.Length > 0 &&
                attr.ConstructorArguments[0].Value is string sectionPath &&
                !string.IsNullOrEmpty(sectionPath))
            {
                sections.Add(sectionPath);
            }
        }

        return sections.ToImmutable();
    }

    private static bool ImplementsStartupStep(INamedTypeSymbol typeSymbol)
    {
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            var fullName = iface.ToDisplayString();
            if (fullName == IStartupStepFullName)
                return true;
        }

        return false;
    }

    private static int GetOrder(INamedTypeSymbol typeSymbol)
    {
        // Look for Order property
        foreach (var member in typeSymbol.GetMembers("Order"))
            if (member is IPropertySymbol { Type.SpecialType: SpecialType.System_Int32 })
                // Try to get constant value if it's a simple getter
                // For now, we'll use 0 as default since getting constant requires more analysis
                // The actual runtime value will be used at startup
                return 0; // Default, actual value determined at runtime

        return 0;
    }
}
