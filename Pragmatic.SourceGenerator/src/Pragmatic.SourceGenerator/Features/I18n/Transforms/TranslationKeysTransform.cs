// =============================================================================
// Pragmatic.Internationalization - TranslationKeysTransform
// Parses JSON translation files and builds the generation model
// Supports multiple folder structures and culture aggregation
// =============================================================================

using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.I18n.Models;

namespace Pragmatic.SourceGenerator.Features.I18n.Transforms;

/// <summary>
///     Transforms JSON translation files into generation models.
///     Supports multiple folder structures:
///     - Structure A: Folder per culture (translations/en/common.json)
///     - Structure B: Flat with suffix (translations/common.en.json)
///     - Structure C: Simple (translations/en.json)
/// </summary>
/// <remarks>
///     <para>
///         This is a partial class split across multiple files for maintainability:
///         <list type="bullet">
///             <item>
///                 <description>TranslationKeysTransform.cs - Main parsing methods and FolderStructure enum (this file)</description>
///             </item>
///             <item>
///                 <description>TranslationKeysTransform.Aggregation.cs - Key aggregation and culture extraction</description>
///             </item>
///             <item>
///                 <description>TranslationKeysTransform.Hierarchy.cs - Hierarchy building for groups</description>
///             </item>
///             <item>
///                 <description>TranslationKeysTransform.Parsing.cs - JSON parsing methods</description>
///             </item>
///             <item>
///                 <description>TranslationKeysTransform.Helpers.cs - Utility methods</description>
///             </item>
///         </list>
///     </para>
/// </remarks>
internal static partial class TranslationKeysTransform
{
    /// <summary>
    ///     Detected folder structure pattern.
    /// </summary>
    public enum FolderStructure
    {
        /// <summary>Folder per culture: translations/en/common.json</summary>
        FolderPerCulture,

        /// <summary>Flat with suffix: translations/common.en.json</summary>
        FlatWithSuffix,

        /// <summary>Simple culture files: translations/en.json</summary>
        Simple
    }

    /// <summary>
    ///     Parses all translation files and aggregates by key.
    /// </summary>
    public static TranslationKeysGenerationModel? ParseAll(
        ImmutableArray<(string Path, string Content)> files,
        TranslationKeysConfiguration config)
    {
        if (files.IsDefaultOrEmpty)
            return null;

        try
        {
            var structure = DetectStructure(files);
            var aggregated = AggregateByKey(files, structure, config);

            if (aggregated.Count == 0)
                return null;

            // A key another key extends would need both a member and a nested class of the same name.
            // It is dropped here and reported as PRAG1805: emitting it produces CS0102 on generated
            // source, which names no translation file and which the author cannot edit.
            var collidingKeys = PrefixCollisions(aggregated);
            foreach (var collidingKey in collidingKeys)
                aggregated.Remove(collidingKey);

            var ns = config.Namespace;
            if (string.IsNullOrEmpty(ns))
                ns = DeriveNamespace(files[0].Path);

            var cultures = aggregated.Values
                .SelectMany(k => k.Translations.Keys)
                .Distinct()
                .OrderBy(c => c == config.DefaultCulture ? 0 : 1)
                .ThenBy(c => c)
                .ToImmutableArray();

            var fileNames = GetFileNames(files, structure);

            var (rootKeys, groups, totalKeys) = config.ByFile
                ? BuildByFileHierarchy(aggregated, fileNames, config)
                : BuildFlatHierarchy(aggregated);

            return new TranslationKeysGenerationModel(
                ns,
                config.ClassName,
                rootKeys,
                groups,
                cultures,
                fileNames,
                files[0].Path,
                totalKeys,
                collidingKeys);
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed translation JSON — skip generation. Other exceptions propagate.
            return null;
        }
    }

    /// <summary>
    ///     Backwards-compatible single-file parsing.
    /// </summary>
    public static TranslationKeysGenerationModel? Parse(
        string json,
        string filePath,
        string className = "T")
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var files = ImmutableArray.Create((filePath, json));
        var config = new TranslationKeysConfiguration
        {
            ClassName = className,
            ByFile = false,
            EmbedTranslations = false
        };

        return ParseAll(files, config);
    }

    /// <summary>
    ///     Detects folder structure from file paths.
    /// </summary>
    public static FolderStructure DetectStructure(ImmutableArray<(string Path, string Content)> files)
    {
        if (files.IsDefaultOrEmpty)
            return FolderStructure.Simple;

        var firstPath = NormalizePath(files[0].Path);

        // Check for Structure A: folder is a culture code (translations/en/common.json)
        var parentDir = Path.GetDirectoryName(firstPath) ?? "";
        var parentDirName = Path.GetFileName(parentDir);
        if (IsCultureCode(parentDirName))
            return FolderStructure.FolderPerCulture;

        // Check for Structure B: filename contains culture suffix (common.en.json)
        var fileName = Path.GetFileNameWithoutExtension(firstPath);
        if (fileName.Contains('.'))
        {
            var lastPart = fileName.Split('.').Last();
            if (IsCultureCode(lastPart))
                return FolderStructure.FlatWithSuffix;
        }

        // Structure C: filename is culture code (en.json)
        return FolderStructure.Simple;
    }
}
