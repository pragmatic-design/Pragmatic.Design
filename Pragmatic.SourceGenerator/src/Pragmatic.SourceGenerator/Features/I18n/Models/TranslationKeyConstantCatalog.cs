using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.I18n.Models;

/// <summary>
///     The constants <c>{ClassName}Keys</c> will hold, for a feature that reads one in an attribute before
///     it exists.
/// </summary>
/// <remarks>
///     <para>
///         The constants are written by this generator, so while any transform runs they do not exist: an
///         attribute argument that names one binds to nothing, and reading it gives no value. A validation
///         rule's <c>MessageKey</c> fell back to its default key that way, in silence, while the compiler —
///         running after the generator — accepted the code.
///     </para>
///     <para>
///         Built from the same model <c>TranslationKeyConstantsTemplate</c> renders, and passed through the
///         pipeline to whoever needs it (docs/CONVENTIONS.md, «Decide at compile time», point 3): the path of a constant here is its
///         path in the generated class by construction.
///     </para>
/// </remarks>
internal sealed record TranslationKeyConstantCatalog(string TypeName, EquatableArray<TranslationKeyConstant> Constants)
{
    /// <summary>No translations: no constants.</summary>
    public static TranslationKeyConstantCatalog Empty { get; } =
        new("", EquatableArray<TranslationKeyConstant>.Empty);

    /// <summary>The name of the constants' class, beside the typed keys' <paramref name="className" />.</summary>
    public static string TypeNameFor(string className) => className + "Keys";

    /// <summary>The catalog of <paramref name="model" />'s constants.</summary>
    public static TranslationKeyConstantCatalog Of(TranslationKeysGenerationModel? model)
    {
        if (model is null)
            return Empty;

        var constants = new List<TranslationKeyConstant>();
        Collect(model.RootKeys, model.Groups, "", constants);
        return new TranslationKeyConstantCatalog(TypeNameFor(model.ClassName), [.. constants]);
    }

    /// <summary>
    ///     The key a reference to a constant holds, as the reference is written in code —
    ///     <c>TKeys.Validation.LeaveRequest.EndsBeforeItStarts</c>, qualified or not — or null when it names
    ///     no constant of this catalog.
    /// </summary>
    public string? KeyOf(string reference)
    {
        if (TypeName.Length == 0)
            return null;

        var text = new string(reference.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (text.StartsWith("global::", StringComparison.Ordinal))
            text = text.Substring("global::".Length);

        var segments = text.Split('.');
        var at = Array.IndexOf(segments, TypeName);
        if (at < 0)
            return null;

        var path = string.Join(".", segments, at + 1, segments.Length - at - 1);
        return Constants.FirstOrDefault(c => c.Path == path)?.Key;
    }

    private static void Collect(
        EquatableArray<TranslationKeyModel> keys, EquatableArray<TranslationKeyGroupModel> groups, string prefix,
        List<TranslationKeyConstant> into)
    {
        foreach (var key in keys)
            into.Add(new TranslationKeyConstant(prefix + key.PropertyName, key.FullKey));

        foreach (var group in groups)
            Collect(group.Keys, group.NestedGroups, prefix + group.ClassName + ".", into);
    }
}
