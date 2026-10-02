using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Glossary.Models;
using Pragmatic.SourceGenerator.Features.Glossary.Templates;

namespace Pragmatic.SourceGenerator.Features.Glossary;

/// <summary>
///     Standalone feature: generates a ubiquitous-language glossary (<c>PragmaticGlossary.Markdown</c>)
///     from <c>[Entity]</c> types and their XML-doc summaries, grouped by namespace. No runtime package
///     dependency — the glossary is a compile-time constant the consumer can surface however it likes.
/// </summary>
internal static class GlossaryFeature
{
    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var generic = Collect(context, "Pragmatic.Persistence.Entity.EntityAttribute`1");
        var nonGeneric = Collect(context, "Pragmatic.Persistence.Entity.EntityAttribute");

        var entries = generic.Collect()
            .Combine(nonGeneric.Collect())
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

        var withAssembly = entries.Combine(
            context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? ""));

        context.RegisterSourceOutputSafe(withAssembly, static (spc, data) =>
        {
            var (models, assemblyName) = data;
            var valid = models.Where(m => m is not null).Select(m => m!).ToImmutableArray();
            if (valid.Length == 0)
                return;

            var artifact = new GlossaryTemplate(valid, assemblyName).RenderOutput();
            if (!artifact.IsEmpty)
                spc.AddSource(artifact);
        });
    }

    private static IncrementalValuesProvider<GlossaryEntryModel?> Collect(
        IncrementalGeneratorInitializationContext context, string attributeMetadataName)
        => context.SyntaxProvider.ForAttributeWithMetadataName(
            attributeMetadataName,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (ctx, _) => ToEntry(ctx.TargetSymbol));

    private static GlossaryEntryModel? ToEntry(ISymbol symbol)
        => symbol is not INamedTypeSymbol type
            ? null
            : new GlossaryEntryModel
            {
                Name = type.Name,
                Namespace = type.ContainingNamespace?.ToDisplayString() ?? "",
                Summary = ExtractSummary(type.GetDocumentationCommentXml())
            };

    private static string? ExtractSummary(string? xml)
    {
        if (string.IsNullOrEmpty(xml))
            return null;

        const string open = "<summary>";
        const string close = "</summary>";
        var start = xml!.IndexOf(open, StringComparison.Ordinal);
        var end = xml.IndexOf(close, StringComparison.Ordinal);
        return start < 0 || end <= start
            ? null
            : xml.Substring(start + open.Length, end - start - open.Length).Trim();
    }
}
