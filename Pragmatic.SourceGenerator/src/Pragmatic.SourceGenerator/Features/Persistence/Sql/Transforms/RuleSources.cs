using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     A Pragmatic visibility rule across assemblies (#502). The engine translates a rule from its <c>ToExpression</c> lambda, which only the
///     assembly declaring the rule has as source: another module reading that rule's entity (<c>[ReadAccess&lt;T&gt;]</c>) sees metadata, with
///     no body. The declaring assembly states each rule's lambda, with every name fully qualified, in
///     <c>[assembly: VisibilityRuleSource&lt;TRule&gt;("…")]</c>; the reading one parses it into a tree of its private compilation, where
///     <see cref="SqlInterceptorGenerator.RuleLambda" /> finds it as it finds a rule written there.
/// </summary>
internal static class RuleSources
{
    private const string RuleBase = "Pragmatic.Persistence.Query.Filters.VisibilityRule";
    private const string Attribute = "Pragmatic.Persistence.Entity.VisibilityRuleSourceAttribute`1";

    /// <summary>The Pragmatic rules this compilation declares, with their lambda qualified to bind anywhere; empty without Persistence's attribute.</summary>
    public static List<(string Rule, string Predicate)> Declared(Compilation compilation)
    {
        var declared = new List<(string, string)>();
        if (compilation.GetTypeByMetadataName(Attribute) is null)
            return declared;
        foreach (var rule in PragmaticNames.AllTypes(compilation.GlobalNamespace).Where(t => t.DeclaringSyntaxReferences.Length > 0 && !t.IsAbstract && EntityOf(t) is not null))
        {
            if (SqlInterceptorGenerator.RuleLambda(rule, compilation, out var model) is { } lambda && model is not null)
                declared.Add((rule.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), new Qualifier(model).Visit(lambda)!.NormalizeWhitespace().ToFullString()));
        }

        return declared;
    }

    /// <summary>
    ///     A tree per rule a referenced assembly declares a source for, when this compilation has no source of that rule: its lambda as the body
    ///     of a <c>ToExpression</c> of the rule's entity, at <see cref="PathOf" />.
    /// </summary>
    public static List<SyntaxTree> Referenced(Compilation compilation, CSharpParseOptions? options)
    {
        var trees = new List<SyntaxTree>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (attribute.AttributeClass is not { Name: "VisibilityRuleSourceAttribute", TypeArguments.Length: 1 } source || source.TypeArguments[0] is not INamedTypeSymbol rule
                    || attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not string predicate
                    || rule.DeclaringSyntaxReferences.Length > 0 || EntityOf(rule) is not { } entity)
                    continue;
                var text = "file static class VisibilityRuleSource\n{\n    internal static global::System.Linq.Expressions.Expression<global::System.Func<"
                           + entity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ", bool>> ToExpression() => " + predicate + ";\n}\n";
                trees.Add(CSharpSyntaxTree.ParseText(text, options, PathOf(rule)));
            }
        }

        return trees;
    }

    /// <summary>The lambda a referenced assembly stated for <paramref name="rule" />, from its tree in <paramref name="compilation" />; null when there is none.</summary>
    public static LambdaExpressionSyntax? Lambda(INamedTypeSymbol rule, Compilation compilation, out SemanticModel? model)
    {
        model = null;
        var path = PathOf(rule);
        if (compilation.SyntaxTrees.FirstOrDefault(t => t.FilePath == path) is not { } tree
            || tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault() is not { ExpressionBody.Expression: LambdaExpressionSyntax lambda })
            return null;
        model = compilation.GetSemanticModel(tree);
        return lambda;
    }

    private static string PathOf(INamedTypeSymbol rule) => "VisibilityRuleSource." + rule.ToDisplayString() + ".g.cs";

    /// <summary>The entity of a Pragmatic rule: the type argument of the <c>VisibilityRule&lt;T&gt;</c> it derives from; null for any other type.</summary>
    private static INamedTypeSymbol? EntityOf(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { Name: "VisibilityRule", TypeArguments.Length: 1 } && current.TypeArguments[0] is INamedTypeSymbol entity && current.ContainingNamespace.ToDisplayString() + ".VisibilityRule" == RuleBase)
                return entity;
        }

        return null;
    }

    /// <summary>
    ///     The lambda with every name it reads written so it binds in any compilation: a type or a namespace fully qualified, a static member
    ///     through its fully qualified type, a constant (not an enum's member) as its value. A member of the rule instance, one the reading
    ///     assembly cannot access, or an extension method called on a value (its namespace is not imported there) is left as it is: it does not
    ///     bind there, and the rule is reported as untranslated where it is read.
    /// </summary>
    private sealed class Qualifier(SemanticModel model) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (IsMemberName(node) || node.IsVar)
                return node;
            switch (model.GetSymbolInfo(node).Symbol)
            {
                case INamedTypeSymbol type:
                    return SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).WithTriviaFrom(node);
                case INamespaceSymbol ns:
                    return SyntaxFactory.ParseName("global::" + ns.ToDisplayString()).WithTriviaFrom(node);
                case IFieldSymbol { IsConst: true, ContainingType.TypeKind: not TypeKind.Enum } constant when Literal(constant) is { } literal:
                    return literal.WithTriviaFrom(node);
                case { IsStatic: true, ContainingType: { } owner } member and (IFieldSymbol or IPropertySymbol or IMethodSymbol):
                    return SyntaxFactory.ParseExpression(owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + member.Name).WithTriviaFrom(node);
                default:
                    return node;
            }
        }

        public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
            => !IsMemberName(node) && model.GetSymbolInfo(node).Symbol is INamedTypeSymbol type
                ? SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).WithTriviaFrom(node)
                : base.VisitGenericName(node);

        /// <summary>The name after a dot (<c>x.Name</c>, <c>A.B</c>): its left side is what gets qualified.</summary>
        private static bool IsMemberName(SimpleNameSyntax node)
            => (node.Parent is MemberAccessExpressionSyntax access && access.Name == node) || (node.Parent is QualifiedNameSyntax qualified && qualified.Right == node);

        private static ExpressionSyntax? Literal(IFieldSymbol constant)
        {
            var value = constant.ConstantValue;
            if (value is null)
                return SyntaxFactory.ParseExpression($"({constant.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})null");
            var text = SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
            var suffix = value switch { decimal => "m", float => "f", double => "d", long => "L", ulong => "UL", uint => "U", _ => "" };
            return value is bool or string or char or int ? SyntaxFactory.ParseExpression(text) : SyntaxFactory.ParseExpression($"(({constant.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){text}{suffix})");
        }
    }
}
