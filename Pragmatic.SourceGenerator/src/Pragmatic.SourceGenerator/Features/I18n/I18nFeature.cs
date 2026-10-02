// =============================================================================
// Pragmatic.SourceGenerator - I18nFeature
// Consolidates the TranslationKeys generator pipeline.
// Country/Currency/Language generators are NOT included here because they rely
// on embedded resources compiled into the standalone Pragmatic.Internationalization.SourceGenerator DLL.
// =============================================================================

using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.I18n.Diagnostics;
using Pragmatic.SourceGenerator.Features.I18n.Models;
using Pragmatic.SourceGenerator.Features.I18n.Templates;
using Pragmatic.SourceGenerator.Features.I18n.Transforms;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.I18n;

internal static partial class I18nFeature
{
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        RegisterTranslationKeys(context, features);
    }

    // ── Translation Keys pipeline (from TranslationKeysGenerator) ──

    private static void RegisterTranslationKeys(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        // 1. Collect all translation JSON files.
        var translationFiles = TranslationFiles(context);

        // 2. Parse configuration from [TranslationKeys] attribute (if present)
        var configProvider = Configuration(context);

        // 3. Get compilation info for debug detection and Composition availability
        var compilationInfo = context.CompilationProvider
            .Select((c, _) => (
                IsDebug: c.Options.OptimizationLevel == OptimizationLevel.Debug,
                HasComposition: HasCompositionReference(c)));

        // 4. Combine all inputs and guard with HasI18n
        var combined = translationFiles
            .Combine(configProvider)
            .Combine(compilationInfo)
            .Combine(DeclaredMessageKeys(context));

        context.RegisterSourceOutputSafe(
            combined.Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasI18n)
                    return;
                var (((filesAndConfig, compInfo), messageKeys), _) = pair;
                var (files, config) = filesAndConfig;
                Generate(ctx, ((files.AsImmutableArray(), config), compInfo), messageKeys);
            });
    }

    private static bool IsTranslationFile(AdditionalText file)
    {
        var path = file.Path.Replace('\\', '/');
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return false;

        // Check for translation folder patterns
        // Structure A: translations/en/common.json
        // Structure B: translations/common.en.json
        // Structure C: translations/en.json
        var parts = path.Split('/');

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var folder = parts[i];
            if (folder.Equals("translations", StringComparison.OrdinalIgnoreCase) ||
                folder.Equals("i18n", StringComparison.OrdinalIgnoreCase) ||
                folder.Equals("locales", StringComparison.OrdinalIgnoreCase) ||
                folder.Equals("lang", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Also match *.translations.json pattern
        var fileName = Path.GetFileName(path);
        return fileName.Contains(".translations.") || fileName.Contains(".i18n.");
    }

    private static TranslationKeysConfiguration ParseConfiguration(GeneratorAttributeSyntaxContext ctx)
    {
        var config = new TranslationKeysConfiguration();

        var attr = ctx.Attributes.FirstOrDefault();
        if (attr == null)
            return config;

        foreach (var arg in attr.NamedArguments)
            switch (arg.Key)
            {
                case "ClassName" when arg.Value.Value is string className:
                    config = config with { ClassName = className };
                    break;
                case "Namespace" when arg.Value.Value is string ns:
                    config = config with { Namespace = ns };
                    break;
                case "EmbedTranslations" when arg.Value.Value is bool embed:
                    config = config with { EmbedTranslations = embed };
                    break;
                case "ByFile" when arg.Value.Value is bool byFile:
                    config = config with { ByFile = byFile };
                    break;
                case "DefaultCulture" when arg.Value.Value is string defaultCulture:
                    config = config with { DefaultCulture = defaultCulture };
                    break;
            }

        return config;
    }

    private static bool HasCompositionReference(Compilation compilation)
    {
        // Check if Pragmatic.Composition is referenced
        foreach (var reference in compilation.References)
        {
            var assembly = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
            if (assembly?.Name == "Pragmatic.Composition")
                return true;
        }

        // Also check by type availability
        var metadataAttribute =
            compilation.GetTypeByMetadataName("Pragmatic.Composition.Attributes.PragmaticMetadataAttribute");
        return metadataAttribute != null;
    }

    private static void Generate(
        SourceProductionContext context,
        ((ImmutableArray<(string Path, string Content)> Files, TranslationKeysConfiguration Config), (bool IsDebug, bool
            HasComposition)) input,
        EquatableArray<DeclaredMessageKey> declaredMessageKeys = default)
    {
        var ((files, config), (isDebug, hasComposition)) = input;

        // ⚠️ Before the guard, and that is the whole change. PRAG1804 is the one check here that does
        // not read a translation file: it needs the error types, which the compilation already gave
        // us. Below the guard it was silent for an application with no translations — the application
        // it exists for — while one that had started translating got told.
        if (files.IsDefaultOrEmpty)
        {
            ReportUntranslatedMessageKeys(context, model: null, declaredMessageKeys);
            return;
        }

        // Validate JSON files and report PRAG1800 for invalid ones
        ReportInvalidFiles(context, files);

        // Report PRAG1803 for files that parse but have no valid keys
        ReportEmptyFiles(context, files);

        // Detect duplicate keys across files for same culture and report PRAG1801
        ReportDuplicateKeys(context, files, config);

        // Detect missing keys across cultures and report PRAG1802
        ReportMissingKeys(context, files, config);

        // Parse and aggregate all translations
        var model = TranslationKeysTransform.ParseAll(files, config);
        if (model == null)
            return;

        // PRAG1805: a key another key extends cannot be a member and a nested class at once. The
        // transform has already left it out, so this report takes the place of the CS0102 that the
        // generated file would otherwise carry.
        foreach (var collidingKey in model.CollidingKeys)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                I18NDiagnostics.KeyIsAlsoAGroup,
                Location.None,
                collidingKey));
        }

        // PRAG1804: a MessageKey nobody translated returns the default text in every language,
        // without failing and without a log.
        ReportUntranslatedMessageKeys(context, model, declaredMessageKeys);

        // Generate main translation keys class
        var template = new TranslationKeysTemplate(model, config);
        var artifact = template.RenderOutput();

        context.AddSource(artifact);

        // The same keys as constants, for the places a property cannot go: attribute arguments.
        context.AddSource(new TranslationKeyConstantsTemplate(model).RenderOutput());

        // The embedded translations as a provider the runtime can read by key — the typed properties
        // above are not one, and t: and IStringLocalizer ask by key.
        string? providerType = null;
        if (config.EmbedTranslations)
        {
            var provider = new EmbeddedTranslationsTemplate(model).RenderOutput();
            if (!provider.IsEmpty)
            {
                context.AddSource(provider);
                providerType = $"global::{model.Namespace}.{EmbeddedTranslationsTemplate.TypeNameFor(model.ClassName)}";
            }
        }

        // Generate metadata for cross-assembly discovery (only if Composition is referenced)
        if (hasComposition)
        {
            var metadataModel = new TranslationsMetadataModel(
                model.Namespace,
                model.ClassName,
                model.Cultures,
                model.Files,
                model.TotalKeys,
                config.DefaultCulture,
                providerType);

            var metadataTemplate = new TranslationsMetadataTemplate(metadataModel, isDebug);
            var metadataArtifact = metadataTemplate.RenderOutput();

            context.AddSource(metadataArtifact);
        }
    }

    private static void ReportInvalidFiles(
        SourceProductionContext context,
        ImmutableArray<(string Path, string Content)> files)
    {
        foreach (var (path, content) in files)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(content);
            }
            catch (JsonException ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    I18NDiagnostics.InvalidTranslationFile,
                    Location.None,
                    Path.GetFileName(path),
                    ex.Message));
            }
        }
    }

    private static void ReportDuplicateKeys(
        SourceProductionContext context,
        ImmutableArray<(string Path, string Content)> files,
        TranslationKeysConfiguration config)
    {
        var structure = TranslationKeysTransform.DetectStructure(files);

        // Track: (culture, key) → first file path
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (path, content) in files)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;

            Dictionary<string, string> keys;
            try
            {
                using var doc = JsonDocument.Parse(content);
                keys = FlattenKeys(doc.RootElement, "");
            }
            catch
            {
                continue;
            }

            var culture = ExtractCultureForDiagnostics(path, structure);

            foreach (var key in keys.Keys)
            {
                var compositeKey = $"{culture}::{key}";
                if (seen.ContainsKey(compositeKey))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        I18NDiagnostics.DuplicateTranslationKey,
                        Location.None,
                        key,
                        culture));
                }
                else
                {
                    seen[compositeKey] = path;
                }
            }
        }
    }

    private static Dictionary<string, string> FlattenKeys(JsonElement element, string prefix)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in element.EnumerateObject())
        {
            var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var nested in FlattenKeys(prop.Value, key))
                    result[nested.Key] = nested.Value;
            }
            else
            {
                result[key] = prop.Value.ToString();
            }
        }

        return result;
    }

    private static string ExtractCultureForDiagnostics(string path, TranslationKeysTransform.FolderStructure structure)
    {
        var normalized = path.Replace('\\', '/');
        return structure switch
        {
            TranslationKeysTransform.FolderStructure.FolderPerCulture =>
                Path.GetFileName(Path.GetDirectoryName(normalized)) ?? "en",
            TranslationKeysTransform.FolderStructure.FlatWithSuffix =>
                Path.GetFileNameWithoutExtension(normalized).Split('.').Last(),
            TranslationKeysTransform.FolderStructure.Simple =>
                Path.GetFileNameWithoutExtension(normalized),
            _ => "en"
        };
    }

    private static void ReportEmptyFiles(
        SourceProductionContext context,
        ImmutableArray<(string Path, string Content)> files)
    {
        foreach (var (path, content) in files)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(content);
                var keys = FlattenKeys(doc.RootElement, "");

                // File parsed OK but has zero string leaf values
                if (keys.Count == 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        I18NDiagnostics.EmptyTranslationFile,
                        Location.None,
                        Path.GetFileName(path)));
                }
            }
            catch
            {
                // Invalid JSON is handled by ReportInvalidFiles; skip here
            }
        }
    }

    private static void ReportMissingKeys(
        SourceProductionContext context,
        ImmutableArray<(string Path, string Content)> files,
        TranslationKeysConfiguration config)
    {
        var structure = TranslationKeysTransform.DetectStructure(files);

        // Collect keys per culture: culture → set of keys
        var keysByCulture = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, content) in files)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;

            Dictionary<string, string> keys;
            try
            {
                using var doc = JsonDocument.Parse(content);
                keys = FlattenKeys(doc.RootElement, "");
            }
            catch
            {
                continue;
            }

            var culture = ExtractCultureForDiagnostics(path, structure);

            if (!keysByCulture.TryGetValue(culture, out var cultureKeys))
            {
                cultureKeys = new HashSet<string>(StringComparer.Ordinal);
                keysByCulture[culture] = cultureKeys;
            }

            foreach (var key in keys.Keys)
                cultureKeys.Add(key);
        }

        // Find the default culture's keys
        var defaultCulture = config.DefaultCulture;
        if (!keysByCulture.TryGetValue(defaultCulture, out var defaultKeys))
            return;

        // Compare default culture keys against each other culture
        foreach (var kvp in keysByCulture)
        {
            if (string.Equals(kvp.Key, defaultCulture, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var key in defaultKeys)
            {
                if (!kvp.Value.Contains(key))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        I18NDiagnostics.MissingTranslationKey,
                        Location.None,
                        key,
                        defaultCulture,
                        kvp.Key));
                }
            }
        }
    }
}
