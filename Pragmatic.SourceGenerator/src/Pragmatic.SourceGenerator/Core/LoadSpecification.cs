using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The member a load's <c>Specification = …</c> names — on <c>[LoadEntity]</c> or <c>[LoadEntities]</c>.
/// </summary>
/// <remarks>
///     <para>
///         Written as <c>nameof(EmployeeSpecifications.InTeam)</c>, the argument's value is only
///         <c>"InTeam"</c>: the class is in the syntax, so the member is the one the compiler bound there. A
///         bare string names a member of <c>{Entity}Specifications</c>, the class every entity already has.
///     </para>
///     <para>
///         Asked by the load's transform, which binds the member's parameters, and by the mapping of a
///         mutation, which must know which of its properties feed the rule rather than the entity — one
///         answer for both.
///     </para>
/// </remarks>
internal static class LoadSpecification
{
    private const string Argument = "Specification";

    /// <summary>The name the attribute's <c>Specification</c> argument carries, or null without one.</summary>
    public static string? NameOf(AttributeData attribute)
        => attribute.NamedArguments.FirstOrDefault(a => a.Key == Argument).Value.Value as string;

    /// <summary>
    ///     The static members the argument names — several when a method is overloaded, none when nothing of
    ///     that name exists. Whether one is a specification of the entity is the caller's to judge.
    /// </summary>
    public static ImmutableArray<ISymbol> Candidates(AttributeData attribute, ITypeSymbol entity, Compilation compilation)
    {
        if (NameOf(attribute) is not { Length: > 0 } name)
            return ImmutableArray<ISymbol>.Empty;

        if (NameOfTarget(attribute) is { } target)
        {
            var info = compilation.GetSemanticModel(target.SyntaxTree).GetSymbolInfo(target);
            var bound = info.Symbol is { } symbol ? ImmutableArray.Create(symbol) : info.CandidateSymbols;
            return bound.Where(IsStaticMember).ToImmutableArray();
        }

        var holder = $"{entity.Name}Specifications";
        return compilation.GetSymbolsWithName(holder, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .SelectMany(type => type.GetMembers(name))
            .Where(IsStaticMember)
            .ToImmutableArray();
    }

    /// <summary>Whether the property's name is a parameter of a rule a load on its type reads by.</summary>
    public static bool FeedsARule(IPropertySymbol property, Compilation compilation)
    {
        foreach (var attribute in property.ContainingType.GetAttributes())
        {
            if (!LoadKey.IsALoad(attribute) || attribute.AttributeClass is not { TypeArguments.Length: 1 } load)
                continue;

            var feeds = Candidates(attribute, load.TypeArguments[0], compilation)
                .OfType<IMethodSymbol>()
                .SelectMany(method => method.Parameters)
                .Any(parameter => string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase));
            if (feeds)
                return true;
        }

        return false;
    }

    /// <summary>The expression inside <c>nameof(…)</c>, when that is how the argument is written.</summary>
    private static ExpressionSyntax? NameOfTarget(AttributeData attribute)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax { ArgumentList: { } arguments })
            return null;

        var argument = arguments.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == Argument);
        return argument?.Expression is InvocationExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
            ArgumentList.Arguments.Count: 1
        } invocation
            ? invocation.ArgumentList.Arguments[0].Expression
            : null;
    }

    private static bool IsStaticMember(ISymbol symbol)
        => symbol.IsStatic && symbol is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IFieldSymbol;
}
