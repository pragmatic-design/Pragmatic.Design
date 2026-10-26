using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Transforms;

/// <summary>
///     The stored members of each trait's child, in the base class's declaration order: one list the EF Core configuration bounds the strings of
///     and the child an engine persists declares itself from. The key (<c>Id</c>, <c>PersistenceId</c>) and the base's abstract foreign key
///     (<c>ParentEntityId</c>) are not here: the configuration maps them on its own, and the engine's child has the shape's key and its parent's
///     foreign key instead.
/// </summary>
/// <remarks>
///     A copy of the base classes' declarations, which live in packages the generator does not reference: <c>TraitColumnsTests</c> compares
///     each list with the public properties of its base class, so a member added there fails a test instead of a database.
/// </remarks>
internal static class TraitColumns
{
    private const string Guid = "global::System.Guid";
    private const string Instant = "global::System.DateTimeOffset";

    /// <summary><c>CommentBase&lt;TId&gt;</c>'s, with its content bounded at <paramref name="maxLength" />.</summary>
    public static EquatableArray<TraitColumnModel> Comment(int maxLength) =>
    [
        new("Content", "string", "\"\"", maxLength, false),
        new("AuthorId", "string?", null, 256, false),
        new("AuthorName", "string?", null, 256, false),
        new("ReplyToId", Guid + "?", null, null, false),
        new("Status", "global::Pragmatic.Comments.CommentStatus", null, null, false),
        new("Visibility", "global::Pragmatic.Comments.CommentVisibility", null, null, false),
        new("IsEdited", "bool", null, null, false),
        new("CreatedAt", Instant, null, null, false),
        new("UpdatedAt", Instant + "?", null, null, false),
        new("UpdatedBy", "string?", null, 256, false),
        new("IsDeleted", "bool", null, null, false),
        new("DeletedAt", Instant + "?", null, null, false),
        new("DeletedBy", "string?", null, 256, false),
        // CommentBase documents Metadata as capped at 4000 and says the generated configuration is what enforces it.
        new("Metadata", "string?", null, 4000, false),
    ];

    /// <summary><c>TagBase</c>'s: the shared tag of a boundary.</summary>
    public static EquatableArray<TraitColumnModel> Tag() =>
    [
        new("Value", "string", "\"\"", 256, true),
        new("DisplayValue", "string", "\"\"", 256, true),
        new("Scope", "string?", null, 128, false),
        new("UsageCount", "int", null, null, false),
        new("CreatedAt", Instant, null, null, false),
        new("CreatedBy", "string?", null, 256, false),
    ];

    /// <summary><c>EntityTagBase&lt;TId&gt;</c>'s: the link of an entity to a tag.</summary>
    public static EquatableArray<TraitColumnModel> TagLink() =>
    [
        new("TagId", Guid, null, null, false),
        new("AddedAt", Instant, null, null, false),
        new("AddedBy", "string?", null, 256, false),
    ];

    /// <summary>
    ///     The tag's value, display value and scope, copied on the link an engine persists: a tag never changes once minted, and a document keeps a
    ///     relation as its key, so the tags of an entity are read off its links without a join. Not on <c>EntityTagBase&lt;TId&gt;</c>, nor with EF Core.
    /// </summary>
    public static EquatableArray<TraitColumnModel> TagLinkCopies() =>
    [
        new("Value", "string", "\"\"", 256, true),
        new("DisplayValue", "string", "\"\"", 256, true),
        new("Scope", "string?", null, 128, false),
    ];

    /// <summary><c>NoteBase&lt;TId&gt;</c>'s, with its content bounded at <paramref name="maxLength" />.</summary>
    public static EquatableArray<TraitColumnModel> Note(int maxLength) =>
    [
        new("Content", "string", "\"\"", maxLength, true),
        new("AuthorId", "string", "\"\"", 256, true),
        new("AuthorName", "string", "\"\"", 256, true),
        new("IsEdited", "bool", null, null, false),
        new("CreatedAt", Instant, null, null, false),
        new("UpdatedAt", Instant + "?", null, null, false),
        new("UpdatedBy", "string?", null, 256, false),
        new("IsDeleted", "bool", null, null, false),
        new("DeletedAt", Instant + "?", null, null, false),
        new("DeletedBy", "string?", null, 256, false),
    ];
}
