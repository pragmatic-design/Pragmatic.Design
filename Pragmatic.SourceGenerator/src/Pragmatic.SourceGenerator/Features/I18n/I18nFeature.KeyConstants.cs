using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.I18n.Models;
using Pragmatic.SourceGenerator.Features.I18n.Transforms;

namespace Pragmatic.SourceGenerator.Features.I18n;

internal static partial class I18nFeature
{
    // NOTE: the attribute lives in the ".Attributes" namespace — the FQN must match it exactly or
    // ForAttributeWithMetadataName never fires and every [TranslationKeys(...)] setting is silently
    // ignored (the generator falls back to defaults). Kept in sync with FeatureDetector.
    private const string TranslationKeysAttributeName = "Pragmatic.Internationalization.Attributes.TranslationKeysAttribute";

    /// <summary>
    ///     The constants <c>{ClassName}Keys</c> will hold, for a feature that reads one in an attribute
    ///     argument while it does not exist yet — a validation rule's <c>MessageKey</c>.
    /// </summary>
    internal static IncrementalValueProvider<TranslationKeyConstantCatalog> KeyConstantCatalog(
        IncrementalGeneratorInitializationContext context)
        => TranslationFiles(context)
            .Combine(Configuration(context))
            .Select(static (pair, _) => TranslationKeyConstantCatalog.Of(
                TranslationKeysTransform.ParseAll(pair.Left.AsImmutableArray(), pair.Right)));

    /// <summary>Every translation JSON file of the compilation, with its text.</summary>
    /// <remarks>
    ///     Wrapped in <c>EquatableArray</c>: a raw <c>ImmutableArray</c> compares by reference, so every
    ///     <c>Collect()</c> produces a non-equal value and the node re-runs (re-parsing all JSON and
    ///     re-reporting diagnostics) on every keystroke. <c>EquatableArray</c> restores caching.
    /// </remarks>
    private static IncrementalValueProvider<EquatableArray<(string Path, string Content)>> TranslationFiles(
        IncrementalGeneratorInitializationContext context)
        => context.AdditionalTextsProvider
            .Where(IsTranslationFile)
            .Select((file, ct) => (
                file.Path,
                Content: file.GetText(ct)?.ToString() ?? ""))
            .Where(f => !string.IsNullOrEmpty(f.Content))
            .Collect()
            .Select((files, _) => new EquatableArray<(string Path, string Content)>(files));

    /// <summary>The configuration <c>[assembly: TranslationKeys(...)]</c> declares, or the default.</summary>
    private static IncrementalValueProvider<TranslationKeysConfiguration> Configuration(
        IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider
            .ForAttributeWithMetadataName(
                TranslationKeysAttributeName,
                static (node, _) => node is CompilationUnitSyntax,
                static (ctx, _) => ParseConfiguration(ctx))
            .Collect()
            .Select((configs, _) => configs.FirstOrDefault() ?? TranslationKeysConfiguration.Default);
}
