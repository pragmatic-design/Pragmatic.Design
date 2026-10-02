using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     The languages the referenced modules' translations are written in, as their Translations metadata
///     declares them.
/// </summary>
/// <param name="Cultures">Every culture a translation file exists for, across the modules, ordered.</param>
/// <param name="DefaultCulture">
///     The culture the translations are written from, when every module that declares one declares the same
///     and has a file for it; <c>null</c> when the modules disagree or none has its own.
/// </param>
internal sealed record DeclaredLanguagesModel(EquatableArray<string> Cultures, string? DefaultCulture)
{
    /// <summary>No module declares translations.</summary>
    public static DeclaredLanguagesModel None { get; } = new(EquatableArray<string>.Empty, null);
}
