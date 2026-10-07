using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Redaction.Models;

namespace Pragmatic.SourceGenerator.Features.Redaction.Transforms;

/// <summary>
///     The classified members a type reaches <b>below its own face</b>, as paths from it.
/// </summary>
/// <remarks>
///     <para>
///         The attribute-driven passes answer "which members of the type carrying the attribute", and
///         that is one level. This one answers "and what does the type hold that declares something of
///         its own" — an owned record, a nested value object, a collection of children — and writes
///         the answer as a path the redactor can walk: <c>Identity.PasswordHash</c>,
///         <c>Members[].Email</c>.
///     </para>
///     <para>
///         ⚠️ <b>It is written here because it cannot be worked out there.</b> JSON carries no types,
///         so a redactor that wanted to descend would have to reflect over the object graph — the one
///         thing that mechanism exists to avoid. The generator has the symbols, including those of
///         referenced assemblies, so the walk happens once at compile time and costs the logging path
///         nothing.
///     </para>
///     <para>
///         <b>Depth is <see cref="MaxDepth" /> and it is a decision, not an accident.</b> Four levels
///         is past any owned record or value object seen in this repository's examples, and the cost
///         is paid by the generator rather than by a log line. A classification deeper than that is
///         not masked, which is the same kind of statement the old behaviour made and the opposite of
///         how it made it: written down, and one level instead of four was not.
///     </para>
///     <para>
///         A cycle ends the branch: a type already on the path is not entered again, so
///         <c>Employee → Manager → Employee</c> terminates. The guard is per branch and not global, so
///         two different members of the same type are both walked.
///     </para>
/// </remarks>
internal static class DeclaredRedactionPathTransform
{
    /// <summary>How far below a type's own face a classification is still collected.</summary>
    /// <remarks>
    ///     Shared with the privacy reader, which walks the same shape to answer a different question —
    ///     see <see cref="OwnedMemberWalk" />. Two copies of the depth and the cycle rule is how the two
    ///     mechanisms came to disagree about what an entity holds.
    /// </remarks>
    public const int MaxDepth = OwnedMemberWalk.MaxDepth;

    private const string NotLoggedReason = "global::Pragmatic.Serialization.RedactionReason.NotLogged";

    private const string PersonalDataReason = "global::Pragmatic.Serialization.RedactionReason.PersonalData";

    /// <summary>The predicate: any type declaration, since anything can hold a classified type.</summary>
    /// <remarks>
    ///     Deliberately not narrowed to "types that already declare something": the case the defect was
    ///     found through is an entity that classifies nothing itself and owns an identity record that
    ///     classifies four members.
    /// </remarks>
    public static bool CouldHoldAClassifiedType(SyntaxNode node, CancellationToken _)
        => node is TypeDeclarationSyntax { Members.Count: > 0 } or RecordDeclarationSyntax;

    /// <summary>Every classified member below this type, as a path, or an empty array when none.</summary>
    public static EquatableArray<RedactedMemberModel> Transform(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, ct) is not INamedTypeSymbol type)
            return EquatableArray<RedactedMemberModel>.Empty;

        var found = ImmutableArray.CreateBuilder<RedactedMemberModel>();

        CollectInherited(type, found);
        Walk(type, type, prefix: "", depth: 0,
            ImmutableHashSet.Create(StringComparer.Ordinal, type.ToDisplayString()), found, ct);

        return found.ToImmutable();
    }

    /// <summary>
    ///     Whether the map will carry an entry for <paramref name="type" />: a member it declares or
    ///     inherits is classified, or one it reaches below its own face is.
    /// </summary>
    /// <remarks>
    ///     The same three sources the map is built from, asked as a yes or no, so that whatever needs
    ///     the set of redacted types (the JSON metadata the redactor serializes them with) cannot
    ///     disagree with the map about which types those are.
    /// </remarks>
    public static bool IsRedacted(INamedTypeSymbol type, CancellationToken ct)
    {
        foreach (var property in OwnedMemberWalk.PropertiesIncludingInherited(type))
        {
            if (Reason(property) is not null)
                return true;
        }

        var found = ImmutableArray.CreateBuilder<RedactedMemberModel>();
        Walk(type, type, prefix: "", depth: 0,
            ImmutableHashSet.Create(StringComparer.Ordinal, type.ToDisplayString()), found, ct);

        return found.Count > 0;
    }

    /// <summary>
    ///     Every path the map will carry for <paramref name="type" />: the members it declares or inherits,
    ///     and those it reaches below its own face.
    /// </summary>
    /// <remarks>
    ///     The map's entry for the type, asked of the symbol rather than of the emitted map: a generated writer
    ///     that masks these paths writes what the declared redactor would have, because both read them from
    ///     here. Empty when the type declares nothing.
    /// </remarks>
    public static ImmutableArray<string> PathsOf(INamedTypeSymbol type, CancellationToken ct)
    {
        var paths = ImmutableArray.CreateBuilder<string>();
        foreach (var property in OwnedMemberWalk.PropertiesIncludingInherited(type))
        {
            if (Reason(property) is not null)
                paths.Add(RedactionNaming.SerializedName(property));
        }

        var found = ImmutableArray.CreateBuilder<RedactedMemberModel>();
        Walk(type, type, prefix: "", depth: 0,
            ImmutableHashSet.Create(StringComparer.Ordinal, type.ToDisplayString()), found, ct);

        foreach (var member in found)
            paths.Add(member.SerializedName);

        return paths.ToImmutable();
    }

    /// <summary>
    ///     What the type itself inherits, filed under the type — not under the base that declared it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The attribute-driven pass files a marked member under its <b>containing</b> type, and the
    ///     lookup is by the runtime type of the object being logged: an entry under
    ///     <c>IdentityRecord</c> answers nothing when a <c>LocalIdentity</c> is serialised. So the
    ///     composed <c>{issuer}|{subject}</c> key, which holds the sign-in address, was declared
    ///     <c>[NotLogged]</c> and masked in nothing.
    ///     <para>
    ///         Only the inherited ones: what the type declares itself the attribute pass already files
    ///         correctly, and the template deduplicates by serialized name per type anyway.
    ///     </para>
    /// </remarks>
    private static void CollectInherited(
        INamedTypeSymbol type, ImmutableArray<RedactedMemberModel>.Builder found)
    {
        foreach (var property in OwnedMemberWalk.PropertiesIncludingInherited(type))
        {
            if (SymbolEqualityComparer.Default.Equals(property.ContainingType, type))
                continue;

            if (Reason(property) is not { } reason)
                continue;

            found.Add(new RedactedMemberModel(
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                RedactionNaming.SerializedName(property),
                reason.Reason,
                reason.Category));
        }
    }

    private static void Walk(
        INamedTypeSymbol owner,
        INamedTypeSymbol current,
        string prefix,
        int depth,
        ImmutableHashSet<string> onThePath,
        ImmutableArray<RedactedMemberModel>.Builder found,
        CancellationToken ct)
    {
        if (depth >= MaxDepth)
            return;

        foreach (var property in OwnedMemberWalk.PropertiesIncludingInherited(current))
        {
            ct.ThrowIfCancellationRequested();

            var (elementType, isCollection) = Unwrap(property.Type);
            if (elementType is not INamedTypeSymbol nested || !CanHoldDeclarations(nested))
                continue;

            var segment = RedactionNaming.SerializedName(property) + (isCollection ? "[]" : "");
            var path = prefix.Length == 0 ? segment : prefix + "." + segment;

            // The members of the nested type itself, at this path. Read from the symbol, so a type
            // from a referenced assembly counts exactly as one declared here — which is the case that
            // matters: the identity record an application owns is the framework's, not its own.
            //
            // ⚠️ Including what it inherits: the identity record's base declares the composed
            // {issuer}|{subject} key, which holds the sign-in address and was masked by nothing.
            foreach (var declared in OwnedMemberWalk.PropertiesIncludingInherited(nested))
            {
                if (Reason(declared) is not { } reason)
                    continue;

                found.Add(new RedactedMemberModel(
                    owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    path + "." + RedactionNaming.SerializedName(declared),
                    reason.Reason,
                    reason.Category));
            }

            var nestedName = nested.ToDisplayString();
            if (onThePath.Contains(nestedName))
                continue;

            Walk(owner, nested, path, depth + 1, onThePath.Add(nestedName), found, ct);
        }
    }

    private static (ITypeSymbol Type, bool IsCollection) Unwrap(ITypeSymbol type)
        => OwnedMemberWalk.Unwrap(type);

    private static bool CanHoldDeclarations(INamedTypeSymbol type)
        => OwnedMemberWalk.CanHoldDeclarations(type);

    private static (string Reason, string? Category)? Reason(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            var ns = attribute.AttributeClass?.ContainingNamespace?.ToDisplayString();

            if (name == "NotLoggedAttribute" && ns == "Pragmatic")
                return (NotLoggedReason, null);

            if (name == "PersonalDataAttribute" && ns == "Pragmatic.Privacy")
                return (PersonalDataReason, attribute.ConstructorArguments.Length > 0
                    ? RedactionNaming.CategoryName(attribute.ConstructorArguments[0])
                    : null);
        }

        return null;
    }
}
