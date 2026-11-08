using System.Collections.Generic;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits;

/// <summary>
///     The permission constants each trait generates for its parent, as (member, value): the one list the permissions templates render from
///     and the permission catalog registers, so the constants a role is granted and the names the catalog lists cannot drift apart.
/// </summary>
internal static class TraitPermissionMembers
{
    /// <summary>The comment permissions: Update only with editing, ViewInternal only with internal notes.</summary>
    public static IReadOnlyList<(string Member, string Value)> Comment(CommentTraitModel model)
    {
        string Slug(string operation) => TraitPermissions.Slug(model.BoundaryName, model.ParentTypeName, TraitPermissions.CommentsGroup, operation);

        var members = new List<(string, string)> { ("Create", Slug(TraitPermissions.Create)), ("Read", Slug(TraitPermissions.Read)) };
        // No Update action/endpoint is generated when editing is off: the constant would advertise a grantable permission that gates nothing.
        if (model.AllowEditing)
            members.Add(("Update", Slug(TraitPermissions.Update)));
        members.Add(("Delete", Slug(TraitPermissions.Delete)));
        // Always: besides gating the moderation endpoint (RequireApproval only), it lets someone edit or delete a comment they did not write.
        members.Add(("Moderate", Slug(TraitPermissions.Moderate)));
        // Who reads and writes comments marked Internal: without it the visibility was a label.
        if (model.SupportInternalNotes)
            members.Add(("ViewInternal", Slug(TraitPermissions.ViewInternal)));
        return members;
    }

    /// <summary>The note permissions: Update only with editing.</summary>
    public static IReadOnlyList<(string Member, string Value)> Note(NoteTraitModel model)
    {
        string Slug(string operation) => TraitPermissions.Slug(model.BoundaryName, model.ParentTypeName, TraitPermissions.NotesGroup, operation);

        var members = new List<(string, string)> { ("Create", Slug(TraitPermissions.Create)), ("Read", Slug(TraitPermissions.Read)) };
        if (model.AllowEditing)
            members.Add(("Update", Slug(TraitPermissions.Update)));
        members.Add(("Delete", Slug(TraitPermissions.Delete)));
        // Lets a moderator edit or delete a note they did not write.
        members.Add(("Moderate", Slug(TraitPermissions.Moderate)));
        return members;
    }

    /// <summary>The tag permissions.</summary>
    public static IReadOnlyList<(string Member, string Value)> Tag(TagTraitModel model)
    {
        string Slug(string operation) => TraitPermissions.Slug(model.BoundaryName, model.ParentTypeName, TraitPermissions.TagsGroup, operation);

        return [("Add", Slug(TraitPermissions.Add)), ("Remove", Slug(TraitPermissions.Remove)), ("Read", Slug(TraitPermissions.Read))];
    }

    /// <summary>The attachment permissions.</summary>
    public static IReadOnlyList<(string Member, string Value)> Attachment(AttachmentTraitModel model)
    {
        string Slug(string operation) => TraitPermissions.Slug(model.BoundaryName, model.ParentTypeName, TraitPermissions.AttachmentsGroup, operation);

        return [("Upload", Slug(TraitPermissions.Upload)), ("Read", Slug(TraitPermissions.Read)), ("Delete", Slug(TraitPermissions.Delete))];
    }
}
