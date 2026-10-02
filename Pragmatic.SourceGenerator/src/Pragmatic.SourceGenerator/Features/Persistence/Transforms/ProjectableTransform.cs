using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transform logic for [Projectable] attribute on expression-bodied properties.
///     Reads the expression body and rewrites member references with a lambda parameter prefix.
/// </summary>
internal static class ProjectableTransform
{
    public static ProjectablePropertyModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not IPropertySymbol propertySymbol)
            return null;

        var containingType = propertySymbol.ContainingType;
        if (containingType is null)
            return null;

        // Must be expression-bodied property
        if (context.TargetNode is not PropertyDeclarationSyntax { ExpressionBody: { } expressionBody })
            return null;

        // Containing type must be partial
        if (!IsPartialType(context.TargetNode.Parent))
            return null;

        ct.ThrowIfCancellationRequested();

        // Rewrite expression body: SubTotal + Tax → e.SubTotal + e.Tax
        var expressionText = ProjectableBody.Rewrite(
            expressionBody.Expression, context.SemanticModel, containingType, "e");

        return new ProjectablePropertyModel
        {
            EntityNamespace = containingType.ContainingNamespace.IsGlobalNamespace
                ? ""
                : containingType.ContainingNamespace.ToDisplayString(),
            EntityTypeName = containingType.Name,
            EntityAccessibility = containingType.DeclaredAccessibility.ToString().ToLowerInvariant(),
            PropertyName = propertySymbol.Name,
            ReturnType = propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ExpressionBody = expressionText,
            PortableBody = ProjectableBody.Rewrite(
                expressionBody.Expression, context.SemanticModel, containingType, ProjectableBody.PortableSource),
            SpecificationsReadingTheRow = SpecificationsReadingTheRow(
                expressionBody.Expression, context.SemanticModel, containingType)
        };
    }

    /// <summary>The specifications the body passes to a query that read the row, for PRAG0735.</summary>
    internal static EquatableArray<SpecificationReadingTheRowModel> SpecificationsReadingTheRow(
        ExpressionSyntax body, SemanticModel model, INamedTypeSymbol entity)
        => [.. ProjectableBody.SpecificationsReadingTheRow(body, model, entity)
            .Select(argument => new SpecificationReadingTheRowModel(
                argument.ToString(), LocationInfo.From(argument.GetLocation())))];

    private static bool IsPartialType(SyntaxNode? node)
    {
        return node switch
        {
            ClassDeclarationSyntax c => c.Modifiers.Any(SyntaxKind.PartialKeyword),
            RecordDeclarationSyntax r => r.Modifiers.Any(SyntaxKind.PartialKeyword),
            StructDeclarationSyntax s => s.Modifiers.Any(SyntaxKind.PartialKeyword),
            _ => false
        };
    }
}
