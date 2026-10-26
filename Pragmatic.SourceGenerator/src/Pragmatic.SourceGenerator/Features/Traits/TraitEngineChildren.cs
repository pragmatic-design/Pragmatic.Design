using System.Collections.Generic;
using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;

namespace Pragmatic.SourceGenerator.Features.Traits;

/// <summary>
///     The children of <c>[HasComments]</c>, <c>[HasNotes]</c>, <c>[HasTags]</c> and <c>[HasAttachments]</c> where the Sql or documents engine persists the parent: the child as a Pragmatic
///     <c>[Entity]</c> (<see cref="TraitEngineEntityTemplate" />), the filter classes its plans name (<see cref="EntityShapeArtifacts.Filters" />,
///     as for a declared entity), and the parent's collection. The same files go to the engines' binding (<see cref="Sources" />): no generator
///     sees another's output, and the engines read the child off its declarations.
/// </summary>
/// <remarks>
///     ASSUMPTION: a compilation that references EF Core keeps the EF Core child, whatever engine a parent's boundary names; a module that mixes
///     engines is the open item 15 of the porting (a relation between engines), not this one.
/// </remarks>
internal static class TraitEngineChildren
{
    /// <summary>True when the children are the engines': an engine's bridge referenced, EF Core's not.</summary>
    public static bool Apply(DetectedFeatures features)
        => !features.HasPersistenceEFCore && (features.HasPersistenceSql || features.HasPersistenceDocuments);

    /// <summary>
    ///     A comment's child, its filters and rules, the parent's <c>Comments</c>, the DTO and the list query; with Actions referenced
    ///     (<paramref name="withActions" />), the actions on the child's repository, their permissions and the moderation queue's query.
    /// </summary>
    public static IEnumerable<Artifact> ForComment(CommentTraitModel model, bool withActions)
    {
        var rules = CommentRules(model);
        yield return new TraitEngineEntityTemplate(new TraitEngineEntityModel(model.ParentNamespace, model.CommentTypeName,
            new TraitEngineParentModel(model.ParentTypeName, model.ParentFullTypeName, model.ParentFkPropertyName, model.IdType), IsSoftDelete: true,
            ConcurrencyAware: false, TraitColumns.Comment(model.MaxLength), EquatableArray<string>.Empty, EquatableArray<TraitEngineReferenceModel>.Empty,
            "[HasComments]", rules.Select(r => r.RuleName).ToEquatableArray())).RenderOutput();
        foreach (var rule in rules)
            yield return new TraitEngineRuleTemplate(rule).RenderOutput();
        foreach (var filter in EntityShapeArtifacts.Filters(Shape(model.ParentNamespace, model.CommentTypeName, model.BoundaryName, model.ParentTypeName, model.ParentIsOwned, model.ParentIsScoped)))
            yield return filter;
        yield return new ParentTraitNavigationTemplate(model).RenderOutput();
        // The read DTO and the list query, which the engine compiles as a declared query of the child.
        yield return new CommentDtoTemplate(model).RenderOutput();
        yield return new CommentListQueryTemplate(model, declaresItsSort: true).RenderOutput();
        if (!withActions)
            yield break;

        foreach (var kind in CommentActionKinds(model))
            yield return new CommentActionsTemplate(model, kind, engine: true).RenderOutput();
        yield return new CommentPermissionsTemplate(model).RenderOutput();
        if (model.RequireApproval)
            yield return new CommentPendingQueryTemplate(model).RenderOutput();
    }

    /// <summary>The comment actions the trait declares, as TraitFeature generates them with EF Core (<c>TraitActionModelBuilder.BuildCommentActions</c>).</summary>
    private static IEnumerable<CommentActionKind> CommentActionKinds(CommentTraitModel model)
    {
        yield return CommentActionKind.Add;
        yield return CommentActionKind.GetById;
        if (model.AllowEditing)
            yield return CommentActionKind.Update;
        yield return CommentActionKind.Delete;
        if (model.RequireApproval)
        {
            yield return CommentActionKind.Moderate;
            yield return CommentActionKind.ListPending;
        }
    }

    /// <summary>The rule nested in a moderated comment an engine persists; the moderator's actions lift it by its name.</summary>
    internal const string ModerationRule = "ModerationRule";

    /// <summary>
    ///     The comment's rules: with moderation, only a visible comment is read (EF Core's named "Moderation" filter); with internal notes, an
    ///     internal comment is read by who holds view-internal (EF Core's <c>InternalVisibilityFilter</c>, the same name and bypass).
    /// </summary>
    private static List<TraitEngineRuleModel> CommentRules(CommentTraitModel model)
    {
        var rules = new List<TraitEngineRuleModel>();
        if (model.RequireApproval)
            rules.Add(new TraitEngineRuleModel(model.ParentNamespace, model.CommentTypeName, ModerationRule,
                "e.Status == global::Pragmatic.Comments.CommentStatus.Visible", null,
                $"Only a visible {model.CommentTypeName} is read: a pending one waits for a moderator, a rejected or hidden one stays out."));
        if (model.SupportInternalNotes)
            rules.Add(new TraitEngineRuleModel(model.ParentNamespace, model.CommentTypeName, "InternalVisibilityFilter",
                "e.Visibility != global::Pragmatic.Comments.CommentVisibility.Internal",
                TraitPermissions.Slug(model.BoundaryName, model.ParentTypeName, TraitPermissions.CommentsGroup, TraitPermissions.ViewInternal),
                $"Hides {model.CommentTypeName} rows marked Internal, except from who holds the view-internal permission."));
        return rules;
    }

    /// <summary>
    ///     A note's child, its filters, the parent's <c>Notes</c>, the DTO and the list query; with Actions referenced
    ///     (<paramref name="withActions" />), the actions on the child's repository and their permissions.
    /// </summary>
    public static IEnumerable<Artifact> ForNote(NoteTraitModel model, bool withActions)
    {
        yield return new TraitEngineEntityTemplate(new TraitEngineEntityModel(model.ParentNamespace, model.NoteTypeName,
            new TraitEngineParentModel(model.ParentTypeName, model.ParentFullTypeName, model.ParentFkPropertyName, model.IdType), IsSoftDelete: true,
            ConcurrencyAware: false, TraitColumns.Note(model.MaxLength), EquatableArray<string>.Empty, EquatableArray<TraitEngineReferenceModel>.Empty,
            "[HasNotes]", EquatableArray<string>.Empty)).RenderOutput();
        foreach (var filter in EntityShapeArtifacts.Filters(Shape(model.ParentNamespace, model.NoteTypeName, model.BoundaryName, model.ParentTypeName, model.ParentIsOwned, model.ParentIsScoped)))
            yield return filter;
        yield return new ParentNoteNavigationTemplate(model).RenderOutput();
        // The read DTO and the list query, which the engine compiles as a declared query of the child.
        yield return new NoteDtoTemplate(model).RenderOutput();
        yield return new NoteListQueryTemplate(model, declaresItsSort: true).RenderOutput();
        if (!withActions)
            yield break;

        yield return new NoteActionsTemplate(model, NoteActionKind.Add, engine: true).RenderOutput();
        yield return new NoteActionsTemplate(model, NoteActionKind.GetById, engine: true).RenderOutput();
        if (model.AllowEditing)
            yield return new NoteActionsTemplate(model, NoteActionKind.Update, engine: true).RenderOutput();
        yield return new NoteActionsTemplate(model, NoteActionKind.Delete, engine: true).RenderOutput();
        yield return new NotePermissionsTemplate(model).RenderOutput();
    }

    /// <summary>
    ///     An entity's links to the boundary's tags: the link (its key the shape's, one link per tag by its unique index, the tag's value copied so
    ///     the list reads it without a join), its filters, the parent's <c>Tags</c>, the DTO and the list query; with Actions referenced
    ///     (<paramref name="withActions" />), the actions on the tag's and the link's repositories and their permissions. On the Sql engine with a
    ///     per-entity limit (<paramref name="serializable" />) the add runs in a serializable transaction, as with EF Core.
    /// </summary>
    public static IEnumerable<Artifact> ForTag(TagTraitModel model, bool withActions, bool serializable)
    {
        var columns = TraitColumns.TagLink().AsImmutableArray().AddRange(TraitColumns.TagLinkCopies().AsImmutableArray());
        yield return new TraitEngineEntityTemplate(new TraitEngineEntityModel(model.ParentNamespace, model.JunctionTypeName,
            new TraitEngineParentModel(model.ParentTypeName, model.ParentFullTypeName, model.ParentFkPropertyName, model.IdType), IsSoftDelete: false,
            ConcurrencyAware: false, columns, [model.ParentFkPropertyName, "TagId"], [new TraitEngineReferenceModel("Tag", model.TagFullTypeName)],
            "[HasTags]", EquatableArray<string>.Empty)).RenderOutput();
        foreach (var filter in EntityShapeArtifacts.Filters(Shape(model.ParentNamespace, model.JunctionTypeName, model.BoundaryName, model.ParentTypeName, model.ParentIsOwned, model.ParentIsScoped, softDelete: false)))
            yield return filter;
        yield return new ParentTagNavigationTemplate(model).RenderOutput();
        yield return new TagDtoTemplate(model).RenderOutput();
        yield return new TagListQueryTemplate(model, declaresItsSort: true).RenderOutput();
        if (!withActions)
            yield break;

        yield return new TagActionsTemplate(model, TagActionKind.Add, engine: true, serializable).RenderOutput();
        yield return new TagActionsTemplate(model, TagActionKind.Remove, engine: true).RenderOutput();
        yield return new TagPermissionsTemplate(model).RenderOutput();
    }

    /// <summary>
    ///     The boundary's shared tag an engine persists: <c>[ConcurrencyAware]</c> (its usage count is raised on the tracked row, a stale save
    ///     refused), unique on its value and scope as with EF Core.
    /// </summary>
    public static Artifact ForSharedTag(TagTraitModel model)
        => new TraitEngineEntityTemplate(new TraitEngineEntityModel(model.ParentNamespace, model.TagTypeName, Parent: null, IsSoftDelete: false,
            ConcurrencyAware: true, TraitColumns.Tag(), ["Value", "Scope"], EquatableArray<TraitEngineReferenceModel>.Empty, "[HasTags]",
            EquatableArray<string>.Empty)).RenderOutput();

    /// <summary>True when the add of a tag runs in a serializable transaction: the Sql engine, alone, and a per-entity limit (as with EF Core).</summary>
    public static bool SerializableTags(TagTraitModel model, DetectedFeatures features)
        => model.MaxPerEntity > 0 && features.HasPersistenceSql && !features.HasPersistenceDocuments;

    /// <summary>
    ///     An attachment's child (with <c>HasThumbnail</c> stored, which the DTO reads by name), its filters, the parent's <c>Attachments</c>, the
    ///     DTO and the list query; with Actions referenced, the actions on the child's repository (the downloads with Endpoints, which their
    ///     response type is from; the thumbnail's with Imaging too) and their permissions; with Jobs, the purge job when the trait asks for one.
    /// </summary>
    public static IEnumerable<Artifact> ForAttachment(AttachmentTraitModel model, DetectedFeatures features)
    {
        var columns = TraitColumns.Attachment().AsImmutableArray().AddRange(TraitColumns.AttachmentCopies().AsImmutableArray());
        yield return new TraitEngineEntityTemplate(new TraitEngineEntityModel(model.ParentNamespace, model.AttachmentTypeName,
            new TraitEngineParentModel(model.ParentTypeName, model.ParentFullTypeName, model.ParentFkPropertyName, model.IdType), IsSoftDelete: true,
            ConcurrencyAware: false, columns, EquatableArray<string>.Empty, EquatableArray<TraitEngineReferenceModel>.Empty,
            "[HasAttachments]", EquatableArray<string>.Empty)).RenderOutput();
        foreach (var filter in EntityShapeArtifacts.Filters(Shape(model.ParentNamespace, model.AttachmentTypeName, model.BoundaryName, model.ParentTypeName, model.ParentIsOwned, model.ParentIsScoped)))
            yield return filter;
        yield return new ParentAttachmentNavigationTemplate(model).RenderOutput();
        yield return new AttachmentDtoTemplate(model).RenderOutput();
        yield return new AttachmentListQueryTemplate(model, declaresItsSort: true).RenderOutput();
        if (features.HasActions)
        {
            var serializable = SerializableAttachments(model, features);
            yield return new AttachmentActionsTemplate(model, AttachmentActionKind.Upload, features.HasImaging, engine: true, serializable).RenderOutput();
            yield return new AttachmentActionsTemplate(model, AttachmentActionKind.GetById, engine: true).RenderOutput();
            if (features.HasEndpoints)
            {
                yield return new AttachmentActionsTemplate(model, AttachmentActionKind.Download, engine: true).RenderOutput();
                if (model.ThumbnailRequested && features.HasImaging)
                    yield return new AttachmentActionsTemplate(model, AttachmentActionKind.DownloadThumbnail, true, engine: true).RenderOutput();
            }
            yield return new AttachmentActionsTemplate(model, AttachmentActionKind.Delete, engine: true).RenderOutput();
            yield return new AttachmentPermissionsTemplate(model).RenderOutput();
        }

        if (features.HasJobs)
            yield return new AttachmentPurgeJobTemplate(model, engine: true).RenderOutput();
    }

    /// <summary>True when the upload counts and inserts in a serializable transaction: the Sql engine, alone, and a per-entity limit (as with EF Core).</summary>
    private static bool SerializableAttachments(AttachmentTraitModel model, DetectedFeatures features)
        => model.MaxPerEntity > 0 && features.HasPersistenceSql && !features.HasPersistenceDocuments;

    /// <summary>The files above, as the engines bind them; empty when the children are EF Core's.</summary>
    public static EquatableArray<ShapeSourceModel> Sources(ImmutableArray<CommentTraitModel> comments, ImmutableArray<NoteTraitModel> notes, ImmutableArray<TagTraitModel> tags,
        ImmutableArray<AttachmentTraitModel> attachments, DetectedFeatures features)
    {
        if (!Apply(features))
            return EquatableArray<ShapeSourceModel>.Empty;
        var sources = new List<ShapeSourceModel>();
        foreach (var comment in comments)
            Add(ForComment(comment, features.HasActions));
        foreach (var note in notes)
            Add(ForNote(note, features.HasActions));
        foreach (var tag in tags)
            Add(ForTag(tag, features.HasActions, SerializableTags(tag, features)));
        foreach (var tag in tags.GroupBy(t => t.TagFullTypeName).Select(g => g.First()))
            Add([ForSharedTag(tag)]);
        foreach (var attachment in attachments)
            Add(ForAttachment(attachment, features));
        return sources.ToEquatableArray();

        void Add(IEnumerable<Artifact> artifacts)
        {
            foreach (var artifact in artifacts)
            {
                if (!artifact.IsEmpty)
                    sources.Add(new ShapeSourceModel(artifact.HintName, artifact.Text));
            }
        }
    }

    /// <summary>What the filter templates read of an entity: its name, its soft delete, its parent (the parent's visibility).</summary>
    private static EntityMetadataModel Shape(string ns, string typeName, string? boundaryName, string parentTypeName, bool parentIsOwned, bool parentIsScoped,
        bool softDelete = true) => new()
    {
        TypeName = typeName,
        FullTypeName = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}",
        Namespace = ns,
        IdType = "System.Guid",
        Accessibility = "public",
        IsValid = true,
        IsFromReference = false,
        BoundaryName = boundaryName,
        IsSoftDelete = softDelete,
        TraitParent = new TraitParentModel(parentTypeName, parentIsOwned, parentIsScoped),
    };
}
