using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     What both walks below a type's own face agree on: how deep to go, what is worth entering, and
///     what a collection is made of.
/// </summary>
/// <remarks>
///     <para>
///         Two features descend into what a type owns and they ask different questions. Redaction asks
///         "what below here is classified", and writes a path the redactor walks at runtime. Privacy
///         asks "what below here is personal data, and what below here is an unclassified string nobody
///         has ruled on" — the second of which is an error. What they must not disagree about is the
///         <em>shape</em> of the walk: a column masked in the logs and absent from the processing
///         register is the defect this exists to prevent, and two copies of these rules is how it
///         comes back.
///     </para>
///     <para>
///         ⚠️ Whether a walk enters a type is not the same question as whether it collects from it.
///         Privacy declines to enter a type that is an entity in its own right — it has its own model,
///         its own route to the subject and its own plan — and that rule belongs to privacy, not here.
///     </para>
/// </remarks>
internal static class OwnedMemberWalk
{
    /// <summary>
    ///     How far below a type's own face a declaration is still seen.
    /// </summary>
    /// <remarks>
    ///     Four levels is past any owned record or value object in this repository's examples, and the
    ///     cost is paid by the generator rather than by a log line or a build. A declaration deeper than
    ///     that is not seen, which is the same statement the one-level behaviour made and the opposite
    ///     of how it made it: written down.
    /// </remarks>
    public const int MaxDepth = 4;

    /// <summary>
    ///     Whether a type is worth entering: a class, record or struct of its own, not a primitive, a
    ///     string, an enum or a framework value like <c>DateTimeOffset</c>.
    /// </summary>
    public static bool CanHoldDeclarations(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None || type.TypeKind == TypeKind.Enum)
            return false;

        // System.* holds no Pragmatic declaration and walking it is pure cost — DateTimeOffset alone
        // has a dozen properties, each of which would be entered.
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";

        return ns != "System" && !ns.StartsWith("System.", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Every public instance property a type <b>has</b> — its own and its bases' — with a member a
    ///     derived type hides counted once, from the derived type.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <c>GetMembers()</c> returns what a type <em>declares</em>. Both walks used it directly,
    ///         so a column on a base class was invisible to them: measured on
    ///         <c>Pragmatic.Identity</c>'s <c>IdentityRecord.ExternalIdentityKey</c>, a composed
    ///         <c>{issuer}|{subject}</c> key holding the sign-in address, which no redaction map masked
    ///         and no <c>PRAG2903</c> asked about while its four siblings on the derived type were both.
    ///     </para>
    ///     <para>
    ///         <b>Derived first, and the name wins.</b> A member re-declared with <c>new</c> is the one
    ///         the compiler binds, so it is the one the application writes and reads; two entries for
    ///         one name would make the answer depend on the order they were found in.
    ///     </para>
    ///     <para>
    ///         Stops where <see cref="CanHoldDeclarations" /> stops, which is what keeps
    ///         <c>System.Object</c> and every framework base out: a ruling cannot be written on a type
    ///         the application does not own.
    ///     </para>
    /// </remarks>
    public static IEnumerable<IPropertySymbol> PropertiesIncludingInherited(INamedTypeSymbol type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = type; current is not null && CanHoldDeclarations(current); current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.DeclaredAccessibility != Accessibility.Public)
                    continue;

                if (seen.Add(property.Name))
                    yield return property;
            }
        }
    }

    /// <summary>
    ///     The bases of <paramref name="type" /> whose members <see cref="PropertiesIncludingInherited" />
    ///     folds into it.
    /// </summary>
    /// <remarks>
    ///     A base that classifies something is not an entity of its own — it has no row and no path to a
    ///     subject, and its members are analysed through whoever inherits them. Named separately because
    ///     the reader has to know which types to drop for that reason.
    /// </remarks>
    public static IEnumerable<INamedTypeSymbol> BasesFoldedInto(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null && CanHoldDeclarations(current); current = current.BaseType)
            yield return current;
    }

    /// <summary>
    ///     The element type behind a collection, and whether there was one.
    /// </summary>
    /// <remarks>
    ///     A string is <c>IEnumerable&lt;char&gt;</c> and is not a collection for this purpose; a
    ///     nullable value type is its underlying type, because neither the payload nor the column
    ///     carries the wrapper.
    /// </remarks>
    public static (ITypeSymbol Type, bool IsCollection) Unwrap(ITypeSymbol type)
    {
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable)
            return (nullable.TypeArguments[0], false);

        if (type.SpecialType == SpecialType.System_String)
            return (type, false);

        if (type is IArrayTypeSymbol array)
            return (array.ElementType, true);

        foreach (var candidate in type.AllInterfaces)
        {
            if (candidate.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                && candidate.TypeArguments.Length == 1)
                return (candidate.TypeArguments[0], true);
        }

        return (type, false);
    }
}
