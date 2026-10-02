using System.Collections.Generic;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Transforms;

/// <summary>One property of a generated trait DTO: enough to both render it and describe it.</summary>
/// <param name="Type">The declared type, fully qualified where ambiguity is possible.</param>
/// <param name="Name">The property name on the DTO.</param>
/// <param name="IsRequired">Whether the value is always present.</param>
/// <param name="IsEnum">Whether it is an enum, which the manifest needs to know.</param>
/// <param name="SourceExpression">
///     Where the value comes from on the entity, relative to the projection's parameter. Defaults to
///     <paramref name="Name"/>; it differs when the DTO flattens a navigation, as the tag DTO does by
///     reading through the junction. Carrying it here is what lets the projection be rendered from
///     this same list instead of being written out a second time.
/// </param>
internal sealed record TraitDtoProperty(
    string Type, string Name, bool IsRequired, bool IsEnum = false, string? SourceExpression = null)
{
    /// <summary>True when the declared type ends in <c>?</c>.</summary>
    public bool IsNullable => Type.EndsWith("?", System.StringComparison.Ordinal);

    /// <summary>The expression the projection reads, defaulting to a property of the same name.</summary>
    public string Source => SourceExpression ?? Name;
}

/// <summary>
///     The shape of the read DTOs the trait feature generates, in one place.
///     <para>
///         The templates render these properties and the manifest describes them. Keeping the list here is
///         what makes the two agree: a DTO the manifest merely <i>names</i> is indistinguishable, for a
///         generated client, from one it has never heard of — both come out as <c>object</c>. A generator
///         cannot rediscover these types by inspecting the compilation, because it is the one creating them.
///     </para>
/// </summary>
internal static class TraitDtoShape
{
    public static IReadOnlyList<TraitDtoProperty> Comment(CommentTraitModel model)
    {
        var properties = new List<TraitDtoProperty>
        {
            new("Guid", "Id", true),
            new(model.SimpleIdType, model.ParentFkPropertyName, true),
            new("string", "Content", true),
            new("string?", "AuthorId", false),
            new("string?", "AuthorName", false)
        };

        if (model.AllowReplies)
            properties.Add(new TraitDtoProperty("Guid?", "ReplyToId", false));

        properties.Add(new TraitDtoProperty("global::Pragmatic.Comments.CommentStatus", "Status", true, IsEnum: true));

        if (model.SupportInternalNotes)
            properties.Add(new TraitDtoProperty("global::Pragmatic.Comments.CommentVisibility", "Visibility", false, IsEnum: true));

        properties.Add(new TraitDtoProperty("bool", "IsEdited", false));
        properties.Add(new TraitDtoProperty("DateTimeOffset", "CreatedAt", true));
        properties.Add(new TraitDtoProperty("DateTimeOffset?", "UpdatedAt", false));

        return properties;
    }

    public static IReadOnlyList<TraitDtoProperty> Note(NoteTraitModel model) =>
    [
        new("Guid", "Id", true),
        new(model.SimpleIdType, model.ParentFkPropertyName, true),
        new("string", "Content", true),
        new("string", "AuthorId", true),
        new("string", "AuthorName", true),
        new("bool", "IsEdited", false),
        new("DateTimeOffset", "CreatedAt", true),
        new("DateTimeOffset?", "UpdatedAt", false)
    ];

    public static IReadOnlyList<TraitDtoProperty> Tag(TagTraitModel model) =>
    [
        new("Guid", "TagId", true),
        new(model.SimpleIdType, model.ParentFkPropertyName, true),
        new("string", "Value", true, SourceExpression: "Tag!.Value"),
        new("string", "DisplayValue", true, SourceExpression: "Tag!.DisplayValue"),
        new("string?", "Scope", false, SourceExpression: "Tag!.Scope"),
        new("DateTimeOffset", "AddedAt", true),
        new("string?", "AddedBy", false)
    ];

    public static IReadOnlyList<TraitDtoProperty> Attachment(AttachmentTraitModel model) =>
    [
        new("Guid", "Id", true),
        new(model.SimpleIdType, model.ParentFkPropertyName, true),
        new("string", "FileName", true),
        new("long", "FileSize", true),
        new("string", "ContentType", true),
        new("string?", "Description", false),
        // Whether there is a thumbnail to ask for, not where it is. A client listing attachments has
        // to know which of them have one — otherwise the generated thumbnail route is reachable only
        // by trying it — and the URI itself is a storage address that no caller may hold: downloads
        // go through the route, which is where the permission is enforced.
        new("bool", "HasThumbnail", true, SourceExpression: "ThumbnailUri != null"),
        new("string", "UploadedBy", true),
        new("DateTimeOffset", "UploadedAt", true)
    ];
}
