using System;
using System.Text;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Owns how a permission constant is named, for the two features that emit them and for the catalog
///     that has to predict what they will emit.
/// </summary>
/// <remarks>
///     <para>
///         A source generator cannot bind a constant it is about to create, so
///         <c>[RequirePermission(CatalogPermissions.RoomType.Read)]</c> is resolved by looking the written
///         path up in a catalog of what this compilation will generate. This type is the one place that
///         correspondence lives, so the emitters and the catalog cannot name the same constant two ways.
///     </para>
///     <para>
///         <b>The path cannot be recovered from the value.</b> <c>EntityPermissionsTemplate</c> emits
///         <c>CatalogPermissions.RoomType</c> for the value <c>"catalog.roomtype"</c>: lowercasing the
///         type name loses the word boundary, and no rule gets <c>RoomType</c> back from <c>roomtype</c>.
///         So the catalog is <b>contributed by the producers</b>, from the same inputs they render from —
///         it does not re-derive paths, which is the mistake that would reintroduce the drift.
///     </para>
///     <para>
///         The two producers therefore name differently, and that is inherent rather than a defect:
///         entity CRUD has the type name and derives the value from it, while a declared permission
///         (<c>[assembly: Permission]</c>) has only the value and derives the path from it. Use
///         <see cref="ForEntityMember" /> for the first and <see cref="FromValue" /> for the second.
///     </para>
/// </remarks>
internal static class PermissionNaming
{
    /// <summary>
    ///     The permission that lets a caller past a row-level filter on <paramref name="typeName" />:
    ///     <c>{boundary}.{entity-kebab}.view-all</c>.
    /// </summary>
    /// <remarks>
    ///     One definition for the four templates that emit it — ownership, scoped, combined, and the
    ///     trait child that borrows its parent's. A private kebab-case conversion in each template would
    ///     let the copies drift apart, and a value spelled two ways is a permission that one of its
    ///     checks never matches.
    /// </remarks>
    public static string ViewAllPermission(string? boundaryName, string typeName)
        => ValueForEntityMember(boundaryName?.ToLowerInvariant(), typeName, ViewAllVerb);

    /// <summary>The verb a bypass permission ends in.</summary>
    /// <remarks>
    ///     Named once because three things spell it: the filters that check it, the constant the
    ///     catalogue emits, and this. Building it here with a second copy of the kebab rule would be
    ///     the drift this whole type exists to stop.
    /// </remarks>
    public const string ViewAllVerb = "view-all";

    /// <summary>Converts PascalCase to kebab-case: <c>RoomType</c> → <c>room-type</c>.</summary>
    public static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>Suffix of the class holding a category's constants — <c>catalog</c> → <c>CatalogPermissions</c>.</summary>
    public const string ClassSuffix = "Permissions";

    /// <summary>Member holding the bare resource value, which has no verb segment of its own.</summary>
    public const string ResourceMember = "Resource";

    /// <summary>Member holding the wildcard value, whose segment cannot be a C# identifier.</summary>
    public const string AllMember = "All";

    private const string Wildcard = "*";

    /// <summary>
    ///     Leading words that name an operation rather than the thing it acts on, so a derived permission
    ///     can be split into resource and verb and read like a written one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A <b>closed</b> list, drawn from the operation names the repository actually declares
    ///         rather than from a general idea of what a verb is. Anything not here is left alone: the
    ///         name then keeps the single-segment shape it had before, so an unrecognised verb cannot
    ///         produce a worse name than the one it replaces.
    ///     </para>
    ///     <para>
    ///         <c>Resource</c> is deliberately absent although it opens 44 type names here: it is the
    ///         prefix the trait generator puts on what it emits, not something the operation does.
    ///     </para>
    /// </remarks>
    private static readonly string[] OperationVerbs =
    [
        // The CRUD vocabulary EntityCrud already emits, so a derived name lands in the same namespace
        // of words as a declared one.
        "Create", "Read", "Update", "Delete",
        // The rest, by descending frequency in the declared operations.
        "Search", "Add", "List", "Get", "Upload", "Remove", "Set", "Refund",
        "Cancel", "Moderate", "Download", "Void", "Restore", "Confirm",
    ];

    /// <summary>
    ///     Words that turn the remainder into a phrase rather than a resource. <c>MarkAsRead</c> split on
    ///     <c>Mark</c> would yield the resource <c>asread</c>, which is worse than not splitting at all.
    /// </summary>
    private static readonly string[] PhraseContinuations = ["As", "To", "For", "From", "With", "By", "In", "On"];

    /// <summary>
    ///     Splits a leading operation verb off a PascalCase operation name, so
    ///     <c>AddGuestComment</c> becomes the resource <c>GuestComment</c> and the verb <c>add</c>.
    ///     Returns <c>false</c> — leaving both outputs untouched — whenever the split would not improve
    ///     the name: unrecognised verb, nothing left after it, or a remainder that reads as a phrase.
    /// </summary>
    public static bool TrySplitLeadingVerb(string typeName, out string resource, out string verb)
    {
        resource = typeName;
        verb = string.Empty;

        foreach (var candidate in OperationVerbs)
        {
            if (typeName.Length <= candidate.Length || !typeName.StartsWith(candidate, StringComparison.Ordinal))
                continue;

            var rest = typeName.Substring(candidate.Length);

            // The character after the verb must start a new word, or the "verb" is just a prefix of a
            // longer one — Sets…, Getter…, Addendum…
            if (!char.IsUpper(rest[0]))
                continue;

            foreach (var continuation in PhraseContinuations)
            {
                if (rest.Length > continuation.Length
                    && rest.StartsWith(continuation, StringComparison.Ordinal)
                    && char.IsUpper(rest[continuation.Length]))
                    return false;
            }

            resource = rest;
            verb = candidate.ToLowerInvariant();
            return true;
        }

        return false;
    }

    /// <summary>
    ///     The CRUD vocabulary an entity permission class emits, member paired with the verb appended to
    ///     the resource prefix. The catalog enumerates this, so what it predicts and what the template
    ///     emits stay the same set — which is the whole point of this type.
    ///
    ///     ⚠️ The template does <b>not</b> enumerate it: it writes each constant out, naming the verb
    ///     from the constants here. So adding an operation means a line there and an entry here, and
    ///     the pair is checked by <c>PermissionCatalogMatchesTemplateTests</c> — which is what caught
    ///     <c>ViewAll</c> being emitted while the catalog still said it would not be.
    /// </summary>
    /// <remarks>
    ///     <see cref="ResourceMember" /> has no verb (it is the bare prefix) and <see cref="AllMember" />
    ///     carries the wildcard. <c>Delete</c> is kept separate — see
    ///     <see cref="EntityDelete" /> — and is emitted for every entity, like these.
    /// </remarks>
    public static readonly (string Member, string? Verb)[] EntityCrud =
    [
        (ResourceMember, null),
        ("Read", ReadVerb),
        ("Create", "create"),
        ("Update", "update"),
        // Reading past the ownership and scope filters. Emitted for every entity rather than only for
        // the ones that carry such a filter, for the reason Delete records below: a constant that
        // appears and disappears with a persistence detail breaks whoever named it.
        ("ViewAll", ViewAllVerb),
        (AllMember, Wildcard),
    ];

    /// <summary>The verb of an entity's read — the value of its CRUD <c>Read</c> constant ends with it.</summary>
    public const string ReadVerb = "read";

    /// <summary>
    ///     The catalogue key under which an entity's read permission is found by the entity, not by a
    ///     constant path: <c>Contoso.Sales.Entities.Order#read</c>.
    /// </summary>
    /// <remarks>
    ///     For a preload's <c>RequireReadPermission</c>, which names an entity and no constant. The <c>#</c>
    ///     keeps it apart from every path a <c>[RequirePermission]</c> argument can spell.
    /// </remarks>
    public static string EntityReadKey(string entityFullTypeName) => entityFullTypeName + "#" + ReadVerb;

    /// <summary>
    ///     The delete operation, emitted for every entity.
    /// </summary>
    /// <remarks>
    ///     Not conditional on the entity being soft-deletable: that would have it backwards, since the
    ///     delete that cannot be undone would be the one with no constant to name it. Kept apart from
    ///     <see cref="EntityCrud" /> rather than folded in so that the two producers can be
    ///     seen to treat it identically; <c>PermissionCatalogMatchesTemplateTests</c> is what checks that
    ///     they do.
    /// </remarks>
    public static readonly (string Member, string Verb) EntityDelete = ("Delete", "delete");

    /// <summary>The members a boundary class carries in its own right, above its nested entities.</summary>
    public static readonly (string Member, string? Verb)[] BoundaryMembers =
    [
        (ResourceMember, null),
        (AllMember, Wildcard),
    ];

    /// <summary>The class name for a category slug — <c>"catalog"</c> → <c>"CatalogPermissions"</c>.</summary>
    public static string ClassNameFor(string categorySlug) => ToPascalCase(categorySlug) + ClassSuffix;

    /// <summary>
    ///     The constant path for a member of an entity's permission class. The caller supplies the type
    ///     name as written in source, because it is the only place the word boundaries still exist.
    /// </summary>
    /// <param name="categorySlug">Boundary slug, or <c>null</c> for an entity outside any boundary.</param>
    /// <param name="typeName">Entity type name as declared — <c>RoomType</c>, not <c>roomtype</c>.</param>
    /// <param name="member">One of <see cref="ResourceMember" />, <see cref="AllMember" />, or a verb.</param>
    public static string ForEntityMember(string? categorySlug, string typeName, string member)
        => string.IsNullOrEmpty(categorySlug)
            ? $"{typeName}{ClassSuffix}.{member}"
            : $"{ClassNameFor(categorySlug!)}.{typeName}.{member}";

    /// <summary>The value that pairs with <see cref="ForEntityMember" /> — the kebab-case dotted form.</summary>
    /// <remarks>
    ///     Kebab, not a flat lowercase, and the same rule for all three producers: entity CRUD, the
    ///     view-all bypass and every trait on the entity. A flat lowercase would give <c>CaseFile</c>
    ///     <c>surveys.casefile.read</c> beside <c>surveys.case-file.…</c> — the same entity, two
    ///     spellings, and invisible on a single-word name.
    /// </remarks>
    public static string ValueForEntityMember(string? categorySlug, string typeName, string? verb)
    {
        var prefix = string.IsNullOrEmpty(categorySlug)
            ? ToKebabCase(typeName)
            : $"{ToKebabCase(categorySlug!)}.{ToKebabCase(typeName)}";

        return verb is null ? prefix : $"{prefix}.{verb}";
    }

    /// <summary>
    ///     The constant path for a declared permission, derived from its value. Valid only
    ///     for that producer, whose template derives the same way — never for entity CRUD, where the
    ///     type name carries word boundaries the value has already lost.
    /// </summary>
    public static string? FromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var segments = value!.Split('.');
        var category = ToPascalCase(segments[0]);
        if (string.IsNullOrEmpty(category))
            return null;

        var className = category + ClassSuffix;
        if (segments.Length == 1)
            return className + "." + ResourceMember;

        var member = MemberFor(segments[segments.Length - 1]);
        if (member is null)
            return null;

        var path = new StringBuilder(className);
        for (var i = 1; i < segments.Length - 1; i++)
        {
            var nested = ToPascalCase(segments[i]);
            if (string.IsNullOrEmpty(nested))
                return null;
            path.Append('.').Append(nested);
        }

        return path.Append('.').Append(member).ToString();
    }

    /// <summary>The member name for a trailing segment, mapping the wildcard onto <see cref="AllMember" />.</summary>
    public static string? MemberFor(string segment)
    {
        if (segment == Wildcard)
            return AllMember;

        var name = ToPascalCase(segment);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    ///     Pascal-cases one dot-separated segment, splitting kebab-case and snake_case so
    ///     <c>"read-all"</c> and <c>"read_all"</c> both become <c>ReadAll</c>.
    /// </summary>
    public static string ToPascalCase(string? segment)
    {
        if (string.IsNullOrEmpty(segment))
            return string.Empty;

        var parts = segment!.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(segment.Length);
        foreach (var part in parts)
        {
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                sb.Append(part, 1, part.Length - 1);
        }

        return sb.ToString();
    }
}
