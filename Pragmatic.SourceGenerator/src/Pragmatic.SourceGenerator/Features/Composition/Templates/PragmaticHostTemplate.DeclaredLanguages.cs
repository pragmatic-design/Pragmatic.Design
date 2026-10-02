// Pragmatic.SourceGenerator - Composition - The languages the modules are translated into

using Microsoft.CodeAnalysis.CSharp;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Registers the modules' declared languages as the lowest culture configuration.
/// </summary>
/// <remarks>
///     Every application with translations wrote <c>UseI18N(i =&gt; { DefaultCulture(EnglishUS);
///     Support(EnglishUS); })</c>, because without a default culture every request failed. The cultures are
///     already declared — they are the translation files — and so is the one they are written from. The
///     application's own configuration still wins; without either, the host now refuses to start.
/// </remarks>
internal sealed partial class PragmaticHostTemplate
{
    private void RenderDeclaredLanguages()
    {
        var languages = _model.DeclaredLanguages;
        if (languages.Cultures.Count == 0)
            return;

        var cultures = string.Join(", ", languages.Cultures.Select(c => SymbolDisplay.FormatLiteral(c, quote: true)));
        var defaultCulture = languages.DefaultCulture is null
            ? "null"
            : SymbolDisplay.FormatLiteral(languages.DefaultCulture, quote: true);

        Comment(languages.DefaultCulture is null
            ? "The languages the modules' translations are written in; they do not agree on a default, so the application configures one"
            : "The languages the modules' translations are written in, and the one they are written from — below the application's configuration");
        AppendLine("services.AddSingleton<global::Pragmatic.Internationalization.Context.II18NConfigProvider>(");
        AppendLine($"    new global::Pragmatic.Internationalization.AspNetCore.Providers.DeclaredLanguagesConfigProvider({defaultCulture}, [{cultures}]));");
    }
}
