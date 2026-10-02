using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.Testing.Comparers.SourceGenerator.Diagnostics;
using Pragmatic.Testing.Comparers.SourceGenerator.Templates;
using Pragmatic.Testing.Comparers.SourceGenerator.Transforms;

namespace Pragmatic.Testing.Comparers.SourceGenerator;

/// <summary>
///     Generates a member-by-member <c>BeEquivalentTo</c> for every
///     <c>[assembly: GenerateComparer&lt;T&gt;]</c> in a test project.
/// </summary>
/// <remarks>
///     <c>Equals</c> answers whether two values differ; this answers <b>where</b>. On a type without
///     value equality it is also the only correct answer — <c>Equals</c> would compare references
///     and fail on two objects holding identical data.
/// </remarks>
[Generator]
public sealed class ComparerGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "Pragmatic.Testing.Assertions.GenerateComparerAttribute`1";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                predicate: static (_, _) => true,
                transform: static (ctx, _) => Extract(ctx))
            .Where(static d => d.Count > 0)
            .Collect();

        context.RegisterSourceOutput(declarations, static (ctx, all) => Emit(ctx, all));
    }

    /// <summary>
    ///     One assembly-level attribute can carry several declarations, so every matching attribute
    ///     on the target is read rather than only the one that triggered the match.
    /// </summary>
    private static EquatableList Extract(GeneratorAttributeSyntaxContext ctx)
    {
        var declared = new List<Declaration>();

        foreach (var attribute in ctx.Attributes)
        {
            if (attribute.AttributeClass?.TypeArguments.FirstOrDefault() is not INamedTypeSymbol target)
                continue;

            declared.Add(new Declaration(target, attribute.ApplicationSyntaxReference));
        }

        return new EquatableList(declared);
    }

    private static void Emit(SourceProductionContext ctx, IEnumerable<EquatableList> all)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var declaration in all.SelectMany(static d => d.Items))
        {
            var target = declaration.Target;
            var location = declaration.SyntaxReference is { } reference
                ? Location.Create(reference.SyntaxTree, reference.Span)
                : Location.None;

            var key = target.ToDisplayString();
            if (!seen.Add(key))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(ComparerDiagnostics.DuplicateDeclaration, location, key));
                continue;
            }

            var model = ComparerTransform.Build(target);
            if (model is null)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(ComparerDiagnostics.NoComparableMembers, location, key));
                continue;
            }

            var artifact = new ComparerTemplate(model).RenderOutput();
            ctx.AddSource(artifact);
        }
    }

    /// <summary>One declared type, and where it was declared.</summary>
    private sealed record Declaration(INamedTypeSymbol Target, SyntaxReference? SyntaxReference);

    /// <summary>
    ///     The declarations of one attribute list, compared by the types they name.
    /// </summary>
    /// <remarks>
    ///     A bare <c>List</c> in the incremental pipeline compares by reference, so every keystroke
    ///     would look like a change and re-run the generator. Symbols compare by identity within a
    ///     compilation, which is what makes this correct as a cache key.
    /// </remarks>
    private sealed class EquatableList(List<Declaration> items) : IEquatable<EquatableList>
    {
        public List<Declaration> Items { get; } = items;

        public int Count => Items.Count;

        public bool Equals(EquatableList? other) =>
            other is not null
            && Items.Count == other.Items.Count
            && Items.Select(static i => i.Target.ToDisplayString())
                .SequenceEqual(other.Items.Select(static i => i.Target.ToDisplayString()), StringComparer.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as EquatableList);

        public override int GetHashCode() =>
            Items.Aggregate(17, static (hash, item) =>
                (hash * 31) + StringComparer.Ordinal.GetHashCode(item.Target.ToDisplayString()));
    }
}
