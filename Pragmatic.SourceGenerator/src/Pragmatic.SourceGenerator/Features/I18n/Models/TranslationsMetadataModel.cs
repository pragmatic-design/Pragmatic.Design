using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.I18n.Models;

/// <summary>
///     Model for [PragmaticMetadata] emission.
/// </summary>
/// <remarks>
///     <c>DefaultCulture</c> is the culture the translations are written from —
///     <c>[TranslationKeys(DefaultCulture = …)]</c>, the one the other cultures' keys are checked against
///     (PRAG1802). The host makes it the default UI culture where the application configures none.
///     <c>ProviderType</c> is the generated <c>ILocalizationProvider</c> over the embedded translations, when
///     the module embeds them: the host registers it, so lookups by key find what the module compiled in.
/// </remarks>
internal sealed record TranslationsMetadataModel(
    string Namespace,
    string ClassName,
    EquatableArray<string> Cultures,
    EquatableArray<string> Files,
    int TotalKeys,
    string DefaultCulture,
    string? ProviderType = null);
