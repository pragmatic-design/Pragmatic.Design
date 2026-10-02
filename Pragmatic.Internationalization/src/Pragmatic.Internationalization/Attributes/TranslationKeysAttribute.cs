// =============================================================================
// Pragmatic.Internationalization - TranslationKeysAttribute
// Assembly-level configuration for translation key generation
// =============================================================================

namespace Pragmatic.Internationalization.Attributes;

/// <summary>
///     Configures translation key generation for this assembly.
/// </summary>
/// <remarks>
///     <para>Apply this attribute at the assembly level to customize how translation keys are generated.</para>
///     <para>Example:</para>
///     <code>
/// [assembly: TranslationKeys(
///     ClassName = "T",
///     ByFile = true,
///     EmbedTranslations = true)]
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class TranslationKeysAttribute : Attribute
{
    /// <summary>
    ///     The root class name for generated translation keys.
    /// </summary>
    /// <remarks>Default: "T"</remarks>
    public string ClassName { get; set; } = "T";

    /// <summary>
    ///     The namespace for generated code.
    /// </summary>
    /// <remarks>Default: derived from project name or first translation file path.</remarks>
    public string Namespace { get; set; } = "";

    /// <summary>
    ///     Whether to embed all translations at compile-time.
    /// </summary>
    /// <remarks>
    ///     <para>When true (default), generates <c>LocalizedString.From(("en", "..."), ("it", "..."))</c></para>
    ///     <para>When false, generates <c>LocalizationKey</c> for runtime lookup.</para>
    /// </remarks>
    public bool EmbedTranslations { get; set; } = true;

    /// <summary>
    ///     Whether to organize translations by source file.
    /// </summary>
    /// <remarks>
    ///     <para>When true (default), creates nested classes per file: T.Common.Key, T.Errors.Key</para>
    ///     <para>When false, all keys are at root level: T.Key</para>
    /// </remarks>
    public bool ByFile { get; set; } = true;

    /// <summary>
    ///     The default culture code for ordering translations.
    /// </summary>
    /// <remarks>Default: "en"</remarks>
    public string DefaultCulture { get; set; } = "en";
}