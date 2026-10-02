using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transform logic for [ComputedFilter] attribute on boolean expression-bodied properties and methods.
///     Reuses <see cref="ProjectableTransform"/> for a property, and the same rewrite for a method.
/// </summary>
internal static class ComputedFilterTransform
{
    /// <summary>
    ///     Intermediate result containing both entity info (for grouping) and member data.
    /// </summary>
    internal sealed record TransformResult(
        string EntityNamespace,
        string EntityTypeName,
        string EntityAccessibility,
        string PropertyName,
        string ExpressionBody)
    {
        /// <summary>
        ///     A method's parameters as the generated members declare them — <c>global::System.DateOnly day</c>
        ///     — or null for a property.
        /// </summary>
        public string? Parameters { get; init; }

        /// <summary>The same parameters passed on as arguments — <c>day</c>.</summary>
        public string? Arguments { get; init; }

        /// <summary>Why the member cannot be generated — the tail of PRAG0733 — or null when it can.</summary>
        public string? Rejection { get; init; }

        /// <summary>The member's declaration, for PRAG0733.</summary>
        public LocationInfo? Location { get; init; }

        /// <summary>The specifications the body passes to a query that read the row: each is PRAG0735.</summary>
        public EquatableArray<SpecificationReadingTheRowModel> SpecificationsReadingTheRow { get; init; } =
            EquatableArray<SpecificationReadingTheRowModel>.Empty;
    }

    public static TransformResult? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is IMethodSymbol method)
            return TransformMethod(context, method, ct);

        // Reuse ProjectableTransform for expression parsing + member rewriting
        var projectable = ProjectableTransform.Transform(context, ct);
        if (projectable is null)
            return null;

        var result = new TransformResult(
            projectable.EntityNamespace,
            projectable.EntityTypeName,
            projectable.EntityAccessibility,
            projectable.PropertyName,
            projectable.ExpressionBody)
        {
            SpecificationsReadingTheRow = projectable.SpecificationsReadingTheRow
        };

        // A filter is a condition: a property of another type generates nothing, so it must not also say nothing.
        return context.TargetSymbol is IPropertySymbol { Type.SpecialType: not SpecialType.System_Boolean } property
            ? result with
            {
                Rejection = $"it is '{property.Type.ToDisplayString()}', and a filter is a bool",
                Location = LocationInfo.From(property.Locations.FirstOrDefault())
            }
            : result;
    }

    /// <summary>
    ///     A method: the rule over the row, and values the caller brings. The body is rewritten as a
    ///     property's is; its parameters bind to themselves, so they stay parameters.
    /// </summary>
    private static TransformResult TransformMethod(
        GeneratorAttributeSyntaxContext context, IMethodSymbol method, CancellationToken ct)
    {
        var entity = method.ContainingType;
        var result = new TransformResult(
            entity.ContainingNamespace.IsGlobalNamespace ? "" : entity.ContainingNamespace.ToDisplayString(),
            entity.Name,
            entity.DeclaredAccessibility.ToString().ToLowerInvariant(),
            method.Name,
            "")
        {
            Location = LocationInfo.From(method.Locations.FirstOrDefault())
        };

        if (RejectionOf(method, context.TargetNode) is { } rejection)
            return result with { Rejection = rejection };

        ct.ThrowIfCancellationRequested();

        var body = ((MethodDeclarationSyntax)context.TargetNode).ExpressionBody!.Expression;
        return result with
        {
            ExpressionBody = ProjectableBody.Rewrite(body, context.SemanticModel, entity, "e"),
            SpecificationsReadingTheRow = ProjectableTransform.SpecificationsReadingTheRow(body, context.SemanticModel, entity),
            Parameters = string.Join(", ", method.Parameters.Select(p =>
                $"{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {Identifier(p.Name)}")),
            Arguments = string.Join(", ", method.Parameters.Select(p => Identifier(p.Name)))
        };
    }

    private static string? RejectionOf(IMethodSymbol method, SyntaxNode node)
    {
        if (method.ReturnType.SpecialType != SpecialType.System_Boolean)
            return $"it returns '{method.ReturnType.ToDisplayString()}', and a filter is a bool";
        if (method.IsStatic)
            return "it is static, and a filter is a rule over the row";
        if (method.IsGenericMethod)
            return "it is generic, and the specification it becomes cannot be";
        if (node is not MethodDeclarationSyntax { ExpressionBody: not null })
            return "it has a block body, and the generator translates an expression body";

        foreach (var parameter in method.Parameters)
        {
            if (parameter.IsParams)
                return $"parameter '{parameter.Name}' is params; the specification takes each value as it is";
            if (parameter.RefKind != RefKind.None)
                return $"parameter '{parameter.Name}' is {parameter.RefKind.ToString().ToLowerInvariant()}; the specification takes each value as it is";
            if (parameter.Name == "e")
                return "parameter 'e' has the name the generated lambda gives the row";
        }

        return null;
    }

    private static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? $"@{name}" : name;
}
