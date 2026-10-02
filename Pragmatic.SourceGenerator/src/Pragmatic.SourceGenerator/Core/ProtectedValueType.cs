using System;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The one place that says what a protected column's type is called.
/// </summary>
/// <remarks>
///     <para>
///         Two generated surfaces have to agree about it: the EF configuration that stores it through
///         <c>ProtectedValueConverter</c>, and the schema metadata that tells migrations the column is
///         binary. A name tested in two places is two facts that can drift — the lesson
///         <see cref="LocalizedStringType" /> was written for.
///     </para>
///     <para>
///         ⚠️ Matched by name and not by symbol, for the same reason as the localized string: the
///         consumers hold a type <em>name</em> by the time they need the answer. The match requires a
///         namespace boundary or an exact match, so a user type called <c>MyProtectedValue</c> is not
///         mistaken for this one.
///     </para>
/// </remarks>
internal static class ProtectedValueType
{
    /// <summary>The fully qualified name, without the <c>global::</c> prefix.</summary>
    public const string FullName = "Pragmatic.Cryptography.ProtectedValue";

    /// <summary>The simple name, which is how a source file usually spells it.</summary>
    public const string SimpleName = "ProtectedValue";

    /// <summary>The converter that maps it to the bytes a column holds.</summary>
    public const string ConverterFullName = "Pragmatic.Cryptography.EFCore.ProtectedValueConverter";

    /// <summary>
    ///     Whether this type name is the protected value, however the caller's model spells it — bare,
    ///     qualified, or with the <c>global::</c> prefix, and nullable either way.
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
