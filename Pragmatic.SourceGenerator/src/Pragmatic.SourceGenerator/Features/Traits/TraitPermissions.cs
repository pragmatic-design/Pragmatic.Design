using System.Text;

namespace Pragmatic.SourceGenerator.Features.Traits;

/// <summary>
///     Single source of truth for the permission slugs of the four traits
///     ([HasComments], [HasTags], [HasNotes], [HasAttachments]).
/// </summary>
/// <remarks>
///     The permission templates emit the constants and the endpoint builder gates the generated
///     endpoints — both must produce the identical string, otherwise a granted permission would not
///     match the one enforced. Slugs are also persisted in consumer databases, so their shape is
///     frozen: never change the segments below.
/// </remarks>
internal static class TraitPermissions
{
    // ── Trait groups ──────────────────────────────────────────────────────
    public const string CommentsGroup = "comments";
    public const string TagsGroup = "tags";
    public const string NotesGroup = "notes";
    public const string AttachmentsGroup = "attachments";

    // ── Operations ────────────────────────────────────────────────────────
    public const string Create = "create";
    public const string Read = "read";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string Moderate = "moderate";
    public const string Upload = "upload";
    public const string Add = "add";
    public const string Remove = "remove";

    /// <summary>Reading and writing comments marked <c>Internal</c> (<c>SupportInternalNotes</c> only).</summary>
    public const string ViewInternal = "view-internal";

    /// <summary>
    ///     Builds the permission slug for a trait operation on a parent entity:
    ///     <c>{boundary}.{entity-kebab}.{group}.{operation}</c>.
    /// </summary>
    public static string Slug(string? boundaryName, string parentTypeName, string group, string operation)
    {
        // Tags predate the "app" fallback and omit the segment entirely when no boundary is known;
        // existing grants depend on that exact shape.
        var prefix = boundaryName is not null
            ? Core.PermissionNaming.ToKebabCase(boundaryName) + "."
            : group == TagsGroup ? string.Empty : "app.";

        return prefix + Core.PermissionNaming.ToKebabCase(parentTypeName) + "." + group + "." + operation;
    }

}
