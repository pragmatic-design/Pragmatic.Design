using System;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The one place that says what a localized field's type is called.
/// </summary>
/// <remarks>
///     <para>
///         Three generated surfaces have to agree about it: the EF value converter that stores it as
///         JSON, the DbContext convention that installs that converter, and the OpenAPI schema that
///         tells a client what the field takes. If each tested the name itself, the halves would be
///         linked by nothing — the entity side could work while the contract described the CLR type
///         instead of the JSON, and no diagnostic could exist because there would be no single fact
///         to disagree with.
///     </para>
///     <para>
///         ⚠️ Matched by name and not by symbol on purpose: two of the three consumers hold a type
///         <em>name</em> (a model string, not a symbol) by the time they need the answer. The name is
///         written once, here. The match requires a namespace boundary or an exact match, so a user
///         type called <c>MyLocalizedString</c> is not mistaken for this one, as an
///         <c>EndsWith("LocalizedString")</c> test would mistake it.
///     </para>
/// </remarks>
internal static class LocalizedStringType
{
    /// <summary>The fully qualified name, without the <c>global::</c> prefix.</summary>
    public const string FullName = "Pragmatic.Internationalization.Types.LocalizedString";

    /// <summary>The simple name, which is how a source file usually spells it.</summary>
    public const string SimpleName = "LocalizedString";

    /// <summary>
    ///     Whether this type name is the localized string, however the caller's model spells it —
    ///     bare, qualified, or with the <c>global::</c> prefix, and nullable either way.
    /// </summary>
    public static bool Matches(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;

        var name = typeName!.TrimEnd('?');
        if (name.StartsWith("global::", StringComparison.Ordinal))
            name = name.Substring("global::".Length);

        return string.Equals(name, SimpleName, StringComparison.Ordinal)
               || name.EndsWith("." + SimpleName, StringComparison.Ordinal);
    }
}
