using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Transforms a <c>[ServiceFactory]</c> class into a <see cref="ServiceFactoryModel" />: the class is a
///     singleton and every <c>[Factory]</c> method registers its return type via a factory that calls it.
/// </summary>
internal static class ServiceFactoryTransform
{
    private const string FactoryMethodAttributeName = "Pragmatic.Composition.Attributes.FactoryAttribute";

    public static ServiceFactoryModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol || typeSymbol.TypeKind != TypeKind.Class)
            return null;

        var methods = ImmutableArray.CreateBuilder<FactoryMethodModel>();

        foreach (var member in typeSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
                continue;

            var factoryAttr = method.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == FactoryMethodAttributeName);
            if (factoryAttr is null)
                continue;

            // The return type is the registered service type; void/no return is not a factory.
            if (method.ReturnsVoid)
                continue;

            var parameters = method.Parameters
                .Select(p => new FactoryParameterModel
                {
                    FullTypeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsOptional = p.IsOptional || p.NullableAnnotation == NullableAnnotation.Annotated,
                    Key = null
                })
                .ToImmutableArray();

            methods.Add(new FactoryMethodModel
            {
                MethodName = method.Name,
                ReturnTypeFullName = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Lifetime = ReadLifetime(factoryAttr),
                Parameters = parameters
            });
        }

        if (methods.Count == 0)
            return null;

        return new ServiceFactoryModel
        {
            Namespace = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : typeSymbol.ContainingNamespace.ToDisplayString(),
            FactoryClassFullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Methods = methods.ToImmutable(),
            Location = LocationInfo.From(typeSymbol.Locations.FirstOrDefault())
        };
    }

    // FactoryAttribute.Lifetime is a ServiceLifetime enum: Singleton=0, Scoped=1, Transient=2. Default Scoped.
    private static string ReadLifetime(AttributeData factoryAttr)
    {
        foreach (var arg in factoryAttr.NamedArguments)
            if (arg.Key == "Lifetime" && arg.Value.Value is int value)
                return value switch
                {
                    0 => "Singleton",
                    1 => "Scoped",
                    2 => "Transient",
                    _ => "Scoped"
                };

        return "Scoped";
    }
}
