using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Transforms [Decorator] attribute declarations into DecoratorModel.
/// </summary>
internal static class DecoratorTransform
{
    /// <summary>
    ///     Transforms a class with [Decorator] attribute into a DecoratorModel.
    /// </summary>
    public static DecoratorModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        // Must implement at least one interface. A [Decorator] with none returns an INVALID model
        // (not null) so CompositionFeature reports PRAG1660 instead of silently dropping it.
        var decoratedInterface = symbol.Interfaces.FirstOrDefault();
        if (decoratedInterface is null)
            return new DecoratorModel
            {
                Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : symbol.ContainingNamespace.ToDisplayString(),
                TypeName = symbol.Name,
                FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                DecoratedInterface = string.Empty,
                Order = 0,
                LocationInfo = LocationInfo.From(symbol.Locations.FirstOrDefault()),
                InvalidReason = InvalidReason.NoInterface
            };

        // Get order from attribute
        var order = GetOrderFromAttribute(context.Attributes.FirstOrDefault());

        // Which interface this decorator decorates, when it implements more than one. The constructor
        // says it: a decorator takes the thing it wraps. Declaration order does not — and inferring from
        // it made a legitimate shape fail, because a decorator implementing IFoo and IBar whose
        // constructor takes IBar was measured against IFoo and reported PRAG1661 ("must have a
        // constructor parameter of the decorated interface type") for the parameter it does have.
        decoratedInterface = InterfaceTakenByAConstructor(symbol) ?? decoratedInterface;

        var hasInnerServiceParameter = symbol.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .Any(c => c.Parameters.Any(p =>
                SymbolEqualityComparer.Default.Equals(p.Type, decoratedInterface)));

        // Extract interface methods for delegation — skip methods already implemented by the dev
        var existingMethods = new HashSet<string>(
            symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsImplicitlyDeclared)
                .Select(m => m.Name));
        var interfaceMethods = ExtractInterfaceMethods(decoratedInterface, existingMethods);

        return new DecoratorModel
        {
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : symbol.ContainingNamespace.ToDisplayString(),
            TypeName = symbol.Name,
            FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            DecoratedInterface = decoratedInterface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Order = order,
            LocationInfo = LocationInfo.From(symbol.Locations.FirstOrDefault()),
            HasInnerServiceParameter = hasInnerServiceParameter,
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            InterfaceMethods = interfaceMethods
        };
    }

    private static ImmutableArray<InterfaceMethodModel> ExtractInterfaceMethods(
        INamedTypeSymbol interfaceSymbol, HashSet<string> existingMethods)
    {
        var builder = ImmutableArray.CreateBuilder<InterfaceMethodModel>();

        // Include methods from the interface and all inherited interfaces
        foreach (var member in interfaceSymbol.GetMembers().Concat(
                     interfaceSymbol.AllInterfaces.SelectMany(i => i.GetMembers())))
        {
            if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
                continue;

            // Skip methods the developer has already implemented
            if (existingMethods.Contains(method.Name))
                continue;

            var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var isAsync = returnType.Contains("Task") || returnType.Contains("ValueTask");
            var isVoid = returnType == "void" ||
                         returnType == "global::System.Threading.Tasks.Task";

            var parameters = string.Join(", ", method.Parameters.Select(p =>
            {
                var paramType = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var defaultVal = p.HasExplicitDefaultValue
                    ? $" = {(p.ExplicitDefaultValue is null ? "default" : p.ExplicitDefaultValue.ToString())}"
                    : "";
                return $"{paramType} {p.Name}{defaultVal}";
            }));

            var parameterNames = string.Join(", ", method.Parameters.Select(p => p.Name));

            builder.Add(new InterfaceMethodModel
            {
                Name = method.Name,
                ReturnType = returnType,
                Parameters = parameters,
                ParameterNames = parameterNames,
                IsAsync = isAsync,
                IsVoid = isVoid
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The implemented interface that a public constructor takes, or <c>null</c> when none does.
    /// </summary>
    /// <remarks>
    ///     Only interesting for a decorator implementing several: with one there is nothing to choose,
    ///     and with none the model is already invalid (PRAG1660). Ambiguity between two that are both
    ///     injected is left to declaration order — the first wins, as before — because the decorator
    ///     would be wrapping both and no rule here can say which one it is "the" decorator for.
    /// </remarks>
    private static INamedTypeSymbol? InterfaceTakenByAConstructor(INamedTypeSymbol symbol)
    {
        if (symbol.Interfaces.Length < 2)
            return null;

        foreach (var candidate in symbol.Interfaces)
        {
            var injected = symbol.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public)
                .Any(c => c.Parameters.Any(p => SymbolEqualityComparer.Default.Equals(p.Type, candidate)));

            if (injected)
                return candidate;
        }

        return null;
    }

    private static int GetOrderFromAttribute(AttributeData? attributeData)
    {
        if (attributeData is null)
            return 0;

        var orderArg = attributeData.NamedArguments.FirstOrDefault(a => a.Key == "Order");
        if (orderArg.Value.Value is int orderValue)
            return orderValue;

        return 0;
    }
}
