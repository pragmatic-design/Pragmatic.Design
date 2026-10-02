using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Specification.Models;

namespace Pragmatic.SourceGenerator.Features.Specification.Transforms;

/// <summary>
///     Reads a <c>static</c> member whose return type is <c>Specification&lt;TEntity&gt;</c>.
/// </summary>
/// <remarks>
///     <para>
///         There is no attribute to hook on, by design, so the pipeline starts from syntax: a static
///         member whose <b>written</b> return type mentions <c>Specification</c>. That test is cheap
///         and wrong on purpose — it lets through <c>ISpecification</c>, a type of the author's own
///         called <c>SpecificationBuilder</c>, anything — and the semantic pass below is what decides.
///         The alternative is asking the compiler about every static member in the compilation on
///         every keystroke.
///     </para>
/// </remarks>
internal static class DeclaredSpecificationTransform
{
    private const string SpecificationMetadataName = "Pragmatic.Specification.Specification`1";

    /// <summary>The syntactic filter: static, and the return type says <c>Specification</c>.</summary>
    public static bool CouldBeASpecification(SyntaxNode node)
        => node switch
        {
            MethodDeclarationSyntax m =>
                m.Modifiers.Any(SyntaxKind.StaticKeyword) && MentionsSpecification(m.ReturnType),
            PropertyDeclarationSyntax p =>
                p.Modifiers.Any(SyntaxKind.StaticKeyword) && MentionsSpecification(p.Type),
            _ => false
        };

    private static bool MentionsSpecification(TypeSyntax type)
        => type.ToString().Contains("Specification<");

    public static DeclaredSpecificationModel? Transform(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var symbol = context.SemanticModel.GetDeclaredSymbol(context.Node, ct);

        var (returnType, parameters, member) = symbol switch
        {
            IMethodSymbol m => (m.ReturnType, m.Parameters, (ISymbol)m),
            IPropertySymbol p => (p.Type, ImmutableArray<IParameterSymbol>.Empty, p),
            _ => (null, ImmutableArray<IParameterSymbol>.Empty, null)
        };

        if (returnType is null || member is null)
            return null;

        // The semantic decision: exactly Pragmatic.Specification.Specification<T>, compared against the
        // symbol the compilation resolves rather than against a spelling. A derived type is not
        // accepted — the generated call returns what the member returns, and widening this would be a
        // second rule nobody declared. Where the assembly is not referenced the lookup answers null and
        // the feature emits nothing, which is the guard: a flag would say the same thing twice.
        var specification = context.SemanticModel.Compilation.GetTypeByMetadataName(SpecificationMetadataName);
        if (specification is null)
            return null;

        if (returnType is not INamedTypeSymbol { IsGenericType: true } named
            || !SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, specification))
            return null;

        if (named.TypeArguments.Length != 1
            || named.TypeArguments[0] is not INamedTypeSymbol entity
            || entity.TypeKind == TypeKind.Error)
            return null;

        var container = member.ContainingType;
        if (container is null || container.IsGenericType)
            return null;

        var ns = container.ContainingNamespace is { IsGlobalNamespace: false } n
            ? n.ToDisplayString()
            : "";

        return new DeclaredSpecificationModel
        {
            EntityFullTypeName = entity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EntityShortName = entity.Name,
            Namespace = ns,
            ContainerFullTypeName = container.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            MemberName = member.Name,
            IsProperty = member is IPropertySymbol,
            IsPubliclyVisible = member.DeclaredAccessibility == Accessibility.Public
                                && container.DeclaredAccessibility == Accessibility.Public,
            Parameters = parameters
                .Select(p => new SpecificationParameter(
                    p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    p.Name))
                .ToImmutableArray()
        };
    }
}
