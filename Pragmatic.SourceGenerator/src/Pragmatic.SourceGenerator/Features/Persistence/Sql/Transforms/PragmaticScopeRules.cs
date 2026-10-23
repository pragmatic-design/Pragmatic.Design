using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Pragmatic's data scope rules (<c>DataScopeRule&lt;T&gt;</c>) as both engines' generators find them: the concrete rules of an entity in the
///     compilation, the T a rule is for, and the strategy it declares. Engine-neutral, linked by the documents generator.
/// </summary>
internal static class PragmaticScopeRules
{
    /// <summary>The concrete <c>DataScopeRule&lt;T&gt;</c> types of the compilation's source whose T is <paramref name="entity" />.</summary>
    public static IEnumerable<INamedTypeSymbol> Of(Compilation compilation, INamedTypeSymbol entity)
        => PragmaticNames.AllTypes(compilation.GlobalNamespace).Where(t => !t.IsAbstract && t.DeclaringSyntaxReferences.Length > 0 && EntityOf(t) is { } of
                                                                            && SymbolEqualityComparer.Default.Equals(of, entity));

    /// <summary>The T of a <c>Pragmatic.Persistence.Scopes.DataScopeRule&lt;T&gt;</c> type; null for any other.</summary>
    public static ITypeSymbol? EntityOf(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { Name: "DataScopeRule", TypeArguments.Length: 1 } && current.ContainingNamespace.ToDisplayString() == "Pragmatic.Persistence.Scopes")
                return current.TypeArguments[0];
        }

        return null;
    }

    /// <summary>
    ///     The strategy a rule declares (<c>Strategy =&gt; ScopeStrategy.Computed</c>): its member's name, <c>Materialized</c> when it does not override
    ///     it (<c>DataScopeRule</c>'s default), null when the override is not a plain member of <c>ScopeStrategy</c> (then it is read as a computed one).
    /// </summary>
    public static string? DeclaredStrategy(INamedTypeSymbol rule)
    {
        for (var current = rule; current is not null && EntityOf(current) is not null; current = current.BaseType)
        {
            if (current.GetMembers("Strategy").OfType<IPropertySymbol>().FirstOrDefault() is not { } property)
                continue;
            var body = property.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<PropertyDeclarationSyntax>().Select(p => p.ExpressionBody?.Expression).FirstOrDefault();
            return body is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : null;
        }

        return "Materialized";
    }

    /// <summary>A rule the reads carry (Computed or Hybrid, or a strategy the source does not name): not one that only the saves apply.</summary>
    public static bool IsRead(INamedTypeSymbol rule) => DeclaredStrategy(rule) != "Materialized";
}
