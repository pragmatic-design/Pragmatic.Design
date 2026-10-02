// =============================================================================
// Pragmatic.Internationalization - Diagnostic Descriptors
// Range: PRAG1800-1899
// =============================================================================

using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.I18n.Diagnostics;

/// <summary>
///     Diagnostic descriptors for Pragmatic.Internationalization source generator.
///     Range: PRAG1800-1899
/// </summary>
internal static class I18NDiagnostics
{
    private const string Category = "Pragmatic.Internationalization";

    /// <summary>
    ///     PRAG1800: Translation JSON file could not be parsed.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidTranslationFile = new(
        id: "PRAG1800",
        title: "Invalid translation file",
        messageFormat: "Translation file '{0}' could not be parsed: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The translation JSON file is malformed or contains invalid content. Ensure the file is valid JSON with string values for translation keys.");

    /// <summary>
    ///     PRAG1804: an error type's MessageKey has no translation.
    /// </summary>
    /// <remarks>
    ///     The resolver uses <c>Error.MessageKey</c> <b>verbatim</b> as the lookup key, with no
    ///     prefix added. A key that matches nothing does not fail: the caller silently receives the
    ///     default English text, in every language. Two consumer projects hit exactly that — one
    ///     wrote <c>error.edition.full</c> against a JSON key of <c>edition.full</c>, the other
    ///     named the file <c>it-IT.json</c> when the context resolves the culture to <c>it</c>.
    ///     Both spent an hour on a failure with no error and no log.
    /// </remarks>
    /// <remarks>
    ///     ⚠️ <b>Info, and the severity is a decision rather than a detail.</b> This runs for an
    ///     application with no translation files at all — the one it exists for — and every
    ///     single-language application is exactly that: it declares error types, translates none, and
    ///     is correct. A warning would fire on correct code, and a diagnostic that fires on correct
    ///     code gets suppressed project-wide and stops being read at all.
    ///     <para>
    ///         The sharper rule — speak only when the host supports more than one culture — is not
    ///         implementable: <c>Support(...)</c> is a runtime call in <c>Program.cs</c>, not a
    ///         declaration, and a generator cannot read it.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MessageKeyNotTranslated = new(
        id: "PRAG1804",
        title: "MessageKey has no translation",
        // ⚠️ The old text ended "no 'error.' prefix is added", which was true of the only shape this
        // check could see — an explicit MessageKey — and false of the one it sees now. An error that
        // overrides Code alone inherits a key the runtime derives WITH that prefix, so the sentence
        // told half its readers the opposite of what had happened. What is true of both: the resolver
        // looks up exactly the key named here, and finds nothing.
        messageFormat: "'{0}' resolves its message under the key '{1}', which no translation file "
            + "defines. The resolver looks that key up verbatim, so the default text is returned in "
            + "every language, silently.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Add the key to the translation files, or correct the MessageKey to match one.");

    /// <summary>
    ///     PRAG1801: Duplicate translation key detected across files.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateTranslationKey = new(
        id: "PRAG1801",
        title: "Duplicate translation key",
        messageFormat: "Translation key '{0}' is defined in multiple files for culture '{1}'; last definition wins",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The same translation key appears in multiple JSON files for the same culture. The last file processed will overwrite earlier definitions. Consider consolidating duplicate keys into a single file.");

    /// <summary>
    ///     PRAG1802: Translation key present in default culture but missing in another culture.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingTranslationKey = new(
        id: "PRAG1802",
        title: "Missing translation key",
        messageFormat: "Translation key '{0}' exists in '{1}' but is missing in '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A translation key is defined in the default culture but missing in another culture. This may cause fallback behavior at runtime. Add the missing key to ensure complete translations.");

    /// <summary>
    ///     PRAG1805: a translation key is also the prefix of another key.
    /// </summary>
    /// <remarks>
    ///     A dotted key becomes one nested class per segment and a member for the last one, so a key
    ///     that another key extends asks for a member and a class of the same name in the same scope.
    ///     That is <c>CS0102</c>, reported on generated source that names no translation file and that
    ///     the author cannot edit. The constant is left out and this takes the compiler error's place:
    ///     an error, because the alternative — generating one of the two keys and staying quiet — is
    ///     the silence this generator exists to remove.
    /// </remarks>
    public static readonly DiagnosticDescriptor KeyIsAlsoAGroup = new(
        id: "PRAG1805",
        title: "Translation key is also a group",
        messageFormat: "Translation key '{0}' is also the prefix of other keys, so the generated class "
            + "would need a member and a nested class of the same name. No constant is generated for it. "
            + "Rename it so that neither key is a prefix of the other — the convention for an error is "
            + "'{0}.detail' for the message and '{0}.title' for the title.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Two translation keys where one is the prefix of the other cannot both become members of the generated class. Rename one of them.");

    /// <summary>
    ///     PRAG1803: Translation file contains no valid translation keys.
    /// </summary>
    public static readonly DiagnosticDescriptor EmptyTranslationFile = new(
        id: "PRAG1803",
        title: "Empty translation file",
        messageFormat: "Translation file '{0}' contains no valid translation keys",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The translation JSON file was parsed successfully but contains no string leaf values. It may contain only nested objects or be effectively empty. Add translation key-value pairs to the file.");
}
