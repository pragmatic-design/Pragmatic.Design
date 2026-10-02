using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.I18n.Models;

/// <summary>
///     Root model for translation keys generation.
///     Record (value-equatable) so the incremental pipeline can cache it.
/// </summary>
internal sealed record TranslationKeysGenerationModel
{
    public TranslationKeysGenerationModel(
        string ns,
        string className,
        EquatableArray<TranslationKeyModel> rootKeys,
        EquatableArray<TranslationKeyGroupModel> groups,
        EquatableArray<string> cultures,
        EquatableArray<string> files,
        string sourceFile,
        int totalKeys,
        EquatableArray<string> collidingKeys = default)
    {
        CollidingKeys = collidingKeys;
        Namespace = ns;
        ClassName = className;
        RootKeys = rootKeys;
        Groups = groups;
        Cultures = cultures;
        Files = files;
        SourceFile = sourceFile;
        TotalKeys = totalKeys;
    }

    /// <summary>
    ///     The root namespace for generated code.
    /// </summary>
    public string Namespace { get; }

    /// <summary>
    ///     The root class name (default: "T").
    /// </summary>
    public string ClassName { get; }

    /// <summary>
    ///     Top-level keys (no nesting).
    /// </summary>
    public EquatableArray<TranslationKeyModel> RootKeys { get; }

    /// <summary>
    ///     Nested groups for hierarchical keys.
    /// </summary>
    public EquatableArray<TranslationKeyGroupModel> Groups { get; }

    /// <summary>
    ///     All cultures found in translation files.
    /// </summary>
    public EquatableArray<string> Cultures { get; }

    /// <summary>
    ///     All file base names (for ByFile mode).
    /// </summary>
    public EquatableArray<string> Files { get; }

    /// <summary>
    ///     Source file that triggered generation (for diagnostics).
    /// </summary>
    public string SourceFile { get; }

    /// <summary>
    ///     Total number of translation keys.
    /// </summary>
    public int TotalKeys { get; }

    /// <summary>
    ///     Keys another key extends, which therefore generate nothing and are reported as PRAG1805.
    /// </summary>
    public EquatableArray<string> CollidingKeys { get; }
}
