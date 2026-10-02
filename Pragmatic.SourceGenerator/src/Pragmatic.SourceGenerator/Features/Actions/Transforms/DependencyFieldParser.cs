using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Reads the private/protected fields of an action or mutation and splits them into injected
///     dependencies and fields whose type cannot be classified either way (reported as PRAG0419).
/// </summary>
internal static class DependencyFieldParser
{
    /// <param name="symbol">The action or mutation whose fields are read.</param>
    /// <param name="boundaryTypeName">
    ///     The fully qualified boundary this operation belongs to, or <c>null</c> when the namespace
    ///     answers for none. A field typed as one of the boundary-keyed services
    ///     (<see cref="BoundaryKeyedServices" />) is marked with it, so the generated invoker receives
    ///     it with <c>[FromKeyedServices]</c> instead of failing to resolve at startup.
    /// </param>
    public static (ImmutableArray<DependencyModel> Dependencies, ImmutableArray<AmbiguousDependencyInfo> Ambiguous)
        Parse(INamedTypeSymbol symbol, string? boundaryTypeName = null)
    {
        var dependencies = ImmutableArray.CreateBuilder<DependencyModel>();
        var ambiguous = ImmutableArray.CreateBuilder<AmbiguousDependencyInfo>();

        foreach (var field in symbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.IsImplicitlyDeclared)
                continue;
            if (field.DeclaredAccessibility is not (Accessibility.Private or Accessibility.Protected))
                continue;

            // The assembly overload: the boundary interfaces of this very module do not exist yet —
            // the same generator emits them later in this compilation — so without it the field's type
            // is an error type, falls to Ambiguous, and is silently not injected.
            switch (ServiceTypeDetector.Classify(field.Type, symbol.ContainingAssembly))
            {
                case ServiceTypeClassification.Service:
                    // The boundary interface of this module does not exist yet, so the symbol cannot
                    // qualify itself: the catalogue that recognised it supplies the namespace.
                    var typeName =
                        ServiceTypeDetector.QualifiedGeneratedService(field.Type, symbol.ContainingAssembly)
                        ?? field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    dependencies.Add(new DependencyModel
                    {
                        FieldName = field.Name,
                        TypeName = typeName,
                        IsReadOnly = field.IsReadOnly,
                        // The two services a boundary registers keyed by its own type. Without the key
                        // the resolution fails at startup naming a generated invoker, which is why
                        // applications reached them through IServiceProvider instead — a runtime lookup
                        // for a fact known here. Where no boundary answers, the field stays unkeyed and
                        // behaves as it did.
                        KeyedServiceType = BoundaryKeyedServices.IsKeyedByBoundary(typeName)
                            ? boundaryTypeName
                            : null,
                        IsInternalType =
                            ServiceTypeDetector.NamesTheInternalBoundaryFacade(
                                field.Type, symbol.ContainingAssembly)
                    });
                    break;

                // A const or static field holds state by construction, so its type being unclassifiable
                // says nothing — only instance fields are candidates for injection and worth a warning.
                case ServiceTypeClassification.Ambiguous when !field.IsStatic && !field.IsConst:
                    ambiguous.Add(new AmbiguousDependencyInfo
                    {
                        FieldName = field.Name,
                        TypeName = field.Type.ToDisplayString()
                    });
                    break;
            }
        }

        return (dependencies.ToImmutable(), ambiguous.ToImmutable());
    }
}
