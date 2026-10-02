using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Reads which boundary types an application can configure — the ones that are, or will be, an
///     <c>IBoundary</c>, which <c>BoundaryConfiguration&lt;TBoundary&gt;</c> is constrained to.
/// </summary>
/// <remarks>
///     <para>
///         The DbContext registration applies what an application passed to <c>UseDatabase</c>, and naming
///         <c>BoundaryConfiguration&lt;T&gt;</c> for a type that is not an <c>IBoundary</c> does not compile
///         (CS0311). Nor could such a configuration exist to be applied. So the line is emitted exactly for
///         the types read here, decided now rather than looked for at run time.
///     </para>
///     <para>
///         Two ways in. A boundary compiled in a referenced assembly carries the interface in its metadata,
///         because the Actions feature emitted it there. A boundary declared in this compilation does not
///         carry it yet — a generator cannot see its own output — so it counts when it is what that feature
///         marks: <c>[Boundary]</c>, <c>partial</c>, in a namespace (<c>BoundaryTransform</c>'s validity).
///         A plain class named by <c>[BelongsTo&lt;T&gt;]</c> is neither, and gets no line.
///     </para>
/// </remarks>
internal static class ConfigurableBoundaryReader
{
    private const string InterfaceName = "Pragmatic.Actions.Boundary.IBoundary";
    private const string AttributeName = "Pragmatic.Actions.Attributes.BoundaryAttribute";

    public static EquatableArray<string> Read(
        Compilation compilation,
        ImmutableArray<EntityMetadataModel> entities,
        CancellationToken ct)
    {
        if (entities.IsDefaultOrEmpty)
            return EquatableArray<string>.Empty;

        var boundaryInterface = compilation.GetTypeByMetadataName(InterfaceName);
        if (boundaryInterface is null)
            return EquatableArray<string>.Empty;

        var boundaryAttribute = compilation.GetTypeByMetadataName(AttributeName);

        var boundaryFqns = entities
            .Select(e => e.BoundaryTypeFullName)
            .Where(fqn => !string.IsNullOrEmpty(fqn))
            .Distinct(StringComparer.Ordinal);

        var result = ImmutableArray.CreateBuilder<string>();
        foreach (var fqn in boundaryFqns)
        {
            ct.ThrowIfCancellationRequested();

            var symbol = compilation.GetTypeByMetadataName(fqn!);
            if (symbol is null)
                continue;

            if (symbol.AllInterfaces.Contains(boundaryInterface, SymbolEqualityComparer.Default)
                || WillBeMarked(symbol, boundaryAttribute))
                result.Add(fqn!);
        }

        result.Sort(StringComparer.Ordinal);
        return new EquatableArray<string>(result.ToImmutable());
    }

    private static bool WillBeMarked(INamedTypeSymbol symbol, INamedTypeSymbol? boundaryAttribute)
    {
        if (boundaryAttribute is null || symbol.ContainingNamespace.IsGlobalNamespace)
            return false;

        if (!symbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, boundaryAttribute)))
            return false;

        return symbol.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(c => c.Modifiers.Any(SyntaxKind.PartialKeyword));
    }
}
