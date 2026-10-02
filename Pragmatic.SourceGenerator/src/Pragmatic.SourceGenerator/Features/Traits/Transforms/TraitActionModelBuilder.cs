using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Transforms;

/// <summary>
/// Builds <see cref="ActionModel"/> instances programmatically from trait models.
/// These flow into the standard ActionsFeature pipeline for Invoker/SetDependencies generation.
/// </summary>
internal static class TraitActionModelBuilder
{
    public static ImmutableArray<ActionModel> BuildCommentActions(CommentTraitModel model)
    {
        var builder = ImmutableArray.CreateBuilder<ActionModel>();
        builder.Add(BuildAddCommentAction(model));
        builder.Add(BuildGetCommentAction(model));

        // AllowEditing = false means the edit must be unreachable, not merely discouraged:
        // no Update action, and (see TraitEndpointModelBuilder) no PUT endpoint either.
        if (model.AllowEditing)
            builder.Add(BuildUpdateCommentAction(model));

        builder.Add(BuildDeleteCommentAction(model));
        if (model.RequireApproval)
        {
            builder.Add(BuildModerateCommentAction(model));
            builder.Add(BuildListPendingCommentsAction(model));
        }
        return builder.ToImmutable();
    }

    private static ActionModel BuildListPendingCommentsAction(CommentTraitModel model)
    {
        var typeName = $"ListPending{model.ParentTypeName}CommentsAction";
        var fullTypeName = string.IsNullOrEmpty(model.ParentNamespace)
            ? typeName : $"{model.ParentNamespace}.{typeName}";
        var dtoFqn = $"global::{model.ParentNamespace}.{model.CommentTypeName}Dto";

        return new ActionModel
        {
            Namespace = model.ParentNamespace,
            TypeName = typeName,
            FullTypeName = $"global::{fullTypeName}",
            Accessibility = "public",
            IsVoid = false,
            ReturnTypeName = $"global::System.Collections.Generic.IReadOnlyList<{dtoFqn}>",
            BelongsToTypeName = model.BoundaryFullTypeName is not null
                ? $"global::{model.BoundaryFullTypeName}" : null,
            SubBoundaryName = model.SubBoundaryName,
            Dependencies = BuildCommentDeps(model),
            InputProperties = ImmutableArray.Create(
                new ActionPropertyModel { Name = $"{model.ParentTypeName}Id", TypeName = model.IdType, IsRequired = true },
                new ActionPropertyModel { Name = "Page", TypeName = "int", IsRequired = false, DefaultValueSyntax = "1" },
                new ActionPropertyModel { Name = "PageSize", TypeName = "int", IsRequired = false, DefaultValueSyntax = "20" }),
            LocationInfo = model.LocationInfo,
            InvalidReason = InvalidReason.None,
        };
    }

    private static ActionModel BuildAddCommentAction(CommentTraitModel model)
    {
        var typeName = $"Add{model.ParentTypeName}CommentAction";
        var fullTypeName = string.IsNullOrEmpty(model.ParentNamespace)
            ? typeName
            : $"{model.ParentNamespace}.{typeName}";

        // Same set as every other comment action — kept in one place so a new dependency cannot be
        // added to the template and forgotten here, which is exactly how _services first went missing
        // from SetDependencies while the field was already being emitted.
        var deps = BuildCommentDeps(model);

        var inputProps = ImmutableArray.CreateBuilder<ActionPropertyModel>();
        inputProps.Add(new ActionPropertyModel
        {
            Name = $"{model.ParentTypeName}Id",
            TypeName = model.IdType,
            IsRequired = true,
        });
        inputProps.Add(new ActionPropertyModel
        {
            Name = "Content",
            TypeName = "string",
            IsRequired = true,
        });

        if (model.AllowReplies)
        {
            inputProps.Add(new ActionPropertyModel
            {
                Name = "ReplyToId",
                TypeName = "System.Guid?",
                IsRequired = false,
                IsNullable = true,
            });
        }

        if (model.SupportInternalNotes)
        {
            inputProps.Add(new ActionPropertyModel
            {
                Name = "Visibility",
                TypeName = "global::Pragmatic.Comments.CommentVisibility",
                IsRequired = false,
                // Fully qualified: this default is rendered verbatim into the boundary interface,
                // which has no using for Pragmatic.Comments.
                DefaultValueSyntax = "global::Pragmatic.Comments.CommentVisibility.Public",
            });
        }

        return new ActionModel
        {
            Namespace = model.ParentNamespace,
            TypeName = typeName,
            FullTypeName = $"global::{fullTypeName}",
            Accessibility = "public",
            IsVoid = false,
            ReturnTypeName = "global::System.Guid",
            BelongsToTypeName = model.BoundaryFullTypeName is not null
                ? $"global::{model.BoundaryFullTypeName}"
                : null,
            SubBoundaryName = model.SubBoundaryName,
            Dependencies = deps,
            InputProperties = inputProps.ToImmutable(),
            LocationInfo = model.LocationInfo,
            InvalidReason = InvalidReason.None,
        };
    }

    private static ActionModel BuildDeleteCommentAction(CommentTraitModel model)
    {
        var typeName = $"Delete{model.ParentTypeName}CommentAction";
        return BuildVoidCommentAction(model, typeName, ImmutableArray.Create(
            new ActionPropertyModel { Name = $"{model.ParentTypeName}Id", TypeName = model.IdType, IsRequired = true },
            new ActionPropertyModel { Name = "CommentId", TypeName = "System.Guid", IsRequired = true }));
    }

    private static ActionModel BuildGetCommentAction(CommentTraitModel model)
    {
        var typeName = $"Get{model.ParentTypeName}CommentAction";
        var fullTypeName = string.IsNullOrEmpty(model.ParentNamespace)
            ? typeName : $"{model.ParentNamespace}.{typeName}";
        var dtoFqn = $"global::{model.ParentNamespace}.{model.CommentTypeName}Dto";

        return new ActionModel
        {
            Namespace = model.ParentNamespace,
            TypeName = typeName,
            FullTypeName = $"global::{fullTypeName}",
            Accessibility = "public",
            IsVoid = false,
            ReturnTypeName = dtoFqn,
            BelongsToTypeName = model.BoundaryFullTypeName is not null
                ? $"global::{model.BoundaryFullTypeName}" : null,
            SubBoundaryName = model.SubBoundaryName,
            Dependencies = BuildCommentDeps(model),
            InputProperties = ImmutableArray.Create(
                new ActionPropertyModel { Name = $"{model.ParentTypeName}Id", TypeName = model.IdType, IsRequired = true },
                new ActionPropertyModel { Name = "CommentId", TypeName = "System.Guid", IsRequired = true }),
            LocationInfo = model.LocationInfo,
            InvalidReason = InvalidReason.None,
        };
    }

    private static ActionModel BuildUpdateCommentAction(CommentTraitModel model)
    {
        var typeName = $"Update{model.ParentTypeName}CommentAction";
        return BuildVoidCommentAction(model, typeName, ImmutableArray.Create(
            new ActionPropertyModel { Name = $"{model.ParentTypeName}Id", TypeName = model.IdType, IsRequired = true },
            new ActionPropertyModel { Name = "CommentId", TypeName = "System.Guid", IsRequired = true },
            new ActionPropertyModel { Name = "Content", TypeName = "string", IsRequired = true }));
    }

    private static ActionModel BuildModerateCommentAction(CommentTraitModel model)
    {
        var typeName = $"Moderate{model.ParentTypeName}CommentAction";
        return BuildVoidCommentAction(model, typeName, ImmutableArray.Create(
            new ActionPropertyModel { Name = $"{model.ParentTypeName}Id", TypeName = model.IdType, IsRequired = true },
            new ActionPropertyModel { Name = "CommentId", TypeName = "System.Guid", IsRequired = true },
            new ActionPropertyModel { Name = "NewStatus", TypeName = "global::Pragmatic.Comments.CommentStatus", IsRequired = true }));
    }

    private static ActionModel BuildVoidCommentAction(
        CommentTraitModel model, string typeName, ImmutableArray<ActionPropertyModel> inputProps)
    {
        var fullTypeName = string.IsNullOrEmpty(model.ParentNamespace)
            ? typeName : $"{model.ParentNamespace}.{typeName}";

        return new ActionModel
        {
            Namespace = model.ParentNamespace,
            TypeName = typeName,
            FullTypeName = $"global::{fullTypeName}",
            Accessibility = "public",
            IsVoid = true,
            BelongsToTypeName = model.BoundaryFullTypeName is not null
                ? $"global::{model.BoundaryFullTypeName}" : null,
            SubBoundaryName = model.SubBoundaryName,
            Dependencies = BuildCommentDeps(model),
            InputProperties = inputProps,
            LocationInfo = model.LocationInfo,
            InvalidReason = InvalidReason.None,
        };
    }

    private static ImmutableArray<DependencyModel> BuildCommentDeps(CommentTraitModel model)
    {
        var boundaryKey = model.BoundaryFullTypeName is not null
            ? $"global::{model.BoundaryFullTypeName}" : null;

        return ImmutableArray.Create(
            new DependencyModel
            {
                FieldName = "_db",
                TypeName = "global::Microsoft.EntityFrameworkCore.DbContext",
                IsReadOnly = true,
                KeyedServiceType = boundaryKey,
            },
            new DependencyModel
            {
                FieldName = "_currentUser",
                TypeName = "global::Pragmatic.Identity.ICurrentUser",
                IsReadOnly = true,
            },
            new DependencyModel
            {
                FieldName = "_clock",
                TypeName = "global::Pragmatic.Temporal.Clock.IClock",
                IsReadOnly = true,
            },
            // Carries the *application* container into the action so an optional ICommentPolicy
            // can be resolved without becoming a required constructor dependency.
            new DependencyModel
            {
                FieldName = "_services",
                TypeName = "global::System.IServiceProvider",
                IsReadOnly = true,
            });
    }

    // ── Tag Actions ────────────────────────────────────────────────────

    public static ImmutableArray<ActionModel> BuildTagActions(TagTraitModel model)
    {
        return ImmutableArray.Create(
            BuildAddTagAction(model),
            BuildRemoveTagAction(model));
    }

    private static ActionModel BuildAddTagAction(TagTraitModel model)
    {
        var typeName = $"Add{model.ParentTypeName}TagAction";
        var fullTypeName = string.IsNullOrEmpty(model.ParentNamespace)
            ? typeName
            : $"{model.ParentNamespace}.{typeName}";

        var boundaryKey = model.BoundaryFullTypeName is not null
            ? $"global::{model.BoundaryFullTypeName}"
            : null;

        return new ActionModel
        {
            Namespace = model.ParentNamespace,
            TypeName = typeName,
            FullTypeName = $"global::{fullTypeName}",
            Accessibility = "public",
            IsVoid = false,
            ReturnTypeName = "global::System.Guid",
            BelongsToTypeName = model.BoundaryFullTypeName is not null
                ? $"global::{model.BoundaryFullTypeName}"
                : null,
            SubBoundaryName = model.SubBoundaryName,
            Dependencies = ImmutableArray.Create(
                new DependencyModel
                {
                    FieldName = "_db",
                    TypeName = "global::Microsoft.EntityFrameworkCore.DbContext",
                    IsReadOnly = true,
                    KeyedServiceType = boundaryKey,
                },
                new DependencyModel
                {
                    FieldName = "_currentUser",
                    TypeName = "global::Pragmatic.Identity.ICurrentUser",
                    IsReadOnly = true,
                },
                new DependencyModel
                {
                    FieldName = "_clock",
                    TypeName = "global::Pragmatic.Temporal.Clock.IClock",
                    IsReadOnly = true,
                },
                // Carries the *application* container into the action so an optional ITagPolicy
                // can be resolved without becoming a required constructor dependency.
                new DependencyModel
                {
                    FieldName = "_services",
                    TypeName = "global::System.IServiceProvider",
                    IsReadOnly = true,
                }),
            InputProperties = ImmutableArray.Create(
                new ActionPropertyModel
                {
                    Name = $"{model.ParentTypeName}Id",
                    TypeName = model.IdType,
                    IsRequired = true,
                },
                new ActionPropertyModel
                {
                    Name = "TagValue",
                    TypeName = "string",
                    IsRequired = true,
                }),
        };
    }

    private static ActionModel BuildRemoveTagAction(TagTraitModel model)
    {
        var typeName = $"Remove{model.ParentTypeName}TagAction";
        var fullTypeName = string.IsNullOrEmpty(model.ParentNamespace)
            ? typeName
            : $"{model.ParentNamespace}.{typeName}";

        var boundaryKey = model.BoundaryFullTypeName is not null
            ? $"global::{model.BoundaryFullTypeName}"
            : null;

        return new ActionModel
        {
            Namespace = model.ParentNamespace,
            TypeName = typeName,
            FullTypeName = $"global::{fullTypeName}",
            Accessibility = "public",
            IsVoid = true,
            BelongsToTypeName = model.BoundaryFullTypeName is not null
                ? $"global::{model.BoundaryFullTypeName}"
                : null,
            SubBoundaryName = model.SubBoundaryName,
            Dependencies = ImmutableArray.Create(
                new DependencyModel
                {
                    FieldName = "_db",
                    TypeName = "global::Microsoft.EntityFrameworkCore.DbContext",
                    IsReadOnly = true,
                    KeyedServiceType = boundaryKey,
                },
                // Same as the add action: the optional ITagPolicy is resolved from here.
                new DependencyModel
                {
                    FieldName = "_services",
                    TypeName = "global::System.IServiceProvider",
                    IsReadOnly = true,
                }),
            InputProperties = ImmutableArray.Create(
                new ActionPropertyModel
                {
                    Name = $"{model.ParentTypeName}Id",
                    TypeName = model.IdType,
                    IsRequired = true,
                },
                new ActionPropertyModel
                {
                    Name = "TagId",
                    TypeName = "System.Guid",
                    IsRequired = true,
                }),
        };
    }

    // ── Note Actions ───────────────────────────────────────────────────

    public static ImmutableArray<ActionModel> BuildNoteActions(NoteTraitModel model)
    {
        var builder = ImmutableArray.CreateBuilder<ActionModel>();
        builder.Add(BuildNoteAction(model, "Add", false, "global::System.Guid"));
        builder.Add(BuildNoteAction(model, "Get", false, $"global::{model.ParentNamespace}.{model.NoteTypeName}Dto"));

        // AllowEditing = false means the edit must be unreachable, not merely discouraged:
        // no Update action, and (see TraitEndpointModelBuilder) no PUT endpoint either.
        if (model.AllowEditing)
            builder.Add(BuildNoteAction(model, "Update", true, null));

        builder.Add(BuildNoteAction(model, "Delete", true, null));
        return builder.ToImmutable();
    }

    private static ActionModel BuildNoteAction(NoteTraitModel model, string kind, bool isVoid, string? returnType)
    {
        var typeName = $"{kind}{model.ParentTypeName}NoteAction";
        var fullTypeName = string.IsNullOrEmpty(model.ParentNamespace) ? typeName : $"{model.ParentNamespace}.{typeName}";
        var boundaryKey = model.BoundaryFullTypeName is not null ? $"global::{model.BoundaryFullTypeName}" : null;

        var deps = ImmutableArray.Create(
            new DependencyModel { FieldName = "_db", TypeName = "global::Microsoft.EntityFrameworkCore.DbContext", IsReadOnly = true, KeyedServiceType = boundaryKey },
            new DependencyModel { FieldName = "_services", TypeName = "global::System.IServiceProvider", IsReadOnly = true },
            new DependencyModel { FieldName = "_currentUser", TypeName = "global::Pragmatic.Identity.ICurrentUser", IsReadOnly = true },
            new DependencyModel { FieldName = "_clock", TypeName = "global::Pragmatic.Temporal.Clock.IClock", IsReadOnly = true });

        var inputProps = ImmutableArray.CreateBuilder<ActionPropertyModel>();
        if (kind is "Add")
        {
            inputProps.Add(new ActionPropertyModel { Name = $"{model.ParentTypeName}Id", TypeName = model.IdType, IsRequired = true });
            inputProps.Add(new ActionPropertyModel { Name = "Content", TypeName = "string", IsRequired = true });
        }
        else if (kind is "Get" or "Delete")
        {
            inputProps.Add(new ActionPropertyModel { Name = $"{model.ParentTypeName}Id", TypeName = model.IdType, IsRequired = true });
            inputProps.Add(new ActionPropertyModel { Name = "NoteId", TypeName = "System.Guid", IsRequired = true });
        }
        else if (kind is "Update")
        {
            inputProps.Add(new ActionPropertyModel { Name = $"{model.ParentTypeName}Id", TypeName = model.IdType, IsRequired = true });
            inputProps.Add(new ActionPropertyModel { Name = "NoteId", TypeName = "System.Guid", IsRequired = true });
            inputProps.Add(new ActionPropertyModel { Name = "Content", TypeName = "string", IsRequired = true });
        }

        return new ActionModel
        {
            Namespace = model.ParentNamespace,
            TypeName = typeName,
            FullTypeName = $"global::{fullTypeName}",
            Accessibility = "public",
            IsVoid = isVoid,
            ReturnTypeName = returnType,
            BelongsToTypeName = boundaryKey,
            SubBoundaryName = model.SubBoundaryName,
            Dependencies = deps,
            InputProperties = inputProps.ToImmutable(),
        };
    }

    // ── Attachment Actions ──────────────────────────────────────────────

    /// <param name="model">The attachment trait model.</param>
    /// <param name="includeDownload">
    ///     False when Pragmatic.Endpoints is absent: the download action's return type
    ///     (<c>FileResponse</c>) lives there, so its class is not generated either.
    /// </param>
    /// <param name="includeThumbnail">
    ///     True only when the attribute asked for a thumbnail and the compilation references
    ///     Pragmatic.Imaging — the same condition that decides whether the class and the route exist.
    /// </param>
    public static ImmutableArray<ActionModel> BuildAttachmentActions(
        AttachmentTraitModel model, bool includeDownload = true, bool includeThumbnail = false)
    {
        var builder = ImmutableArray.CreateBuilder<ActionModel>();
        builder.Add(BuildAttachmentAction(model, "Upload", false, "global::System.Guid"));
        builder.Add(BuildAttachmentAction(model, "Get", false, $"global::{model.ParentNamespace}.{model.AttachmentTypeName}Dto"));
        if (includeDownload)
            builder.Add(BuildAttachmentAction(model, "Download", false, "global::Pragmatic.Endpoints.Responses.FileResponse"));

        // ⚠️ The action's CLASS is generated by AttachmentActionsTemplate; this is what gives it an
        // invoker and a DI registration. Emitting one without the other produces a route that maps,
        // resolves nothing and answers 500 — measured, on the first end-to-end run of this feature.
        if (includeThumbnail)
            builder.Add(BuildAttachmentAction(model, "DownloadThumbnail", false, "global::Pragmatic.Endpoints.Responses.FileResponse"));

        builder.Add(BuildAttachmentAction(model, "Delete", true, null));
        return builder.ToImmutable();
    }

    private static ActionModel BuildAttachmentAction(AttachmentTraitModel model, string kind, bool isVoid, string? returnType)
    {
        var typeName = $"{kind}{model.ParentTypeName}AttachmentAction";
        var fullTypeName = string.IsNullOrEmpty(model.ParentNamespace) ? typeName : $"{model.ParentNamespace}.{typeName}";
        var boundaryKey = model.BoundaryFullTypeName is not null ? $"global::{model.BoundaryFullTypeName}" : null;

        var deps = ImmutableArray.CreateBuilder<DependencyModel>();
        deps.Add(new DependencyModel { FieldName = "_db", TypeName = "global::Microsoft.EntityFrameworkCore.DbContext", IsReadOnly = true, KeyedServiceType = boundaryKey });
        // Keep in lockstep with AttachmentActionsTemplate.RenderBody: a field declared there and not
        // listed here is never assigned (NullReferenceException at runtime), and the reverse produces
        // a SetDependencies parameter for a field that does not exist (CS0103).
        if (kind is "Upload" or "Delete")
        {
            deps.Add(new DependencyModel { FieldName = "_currentUser", TypeName = "global::Pragmatic.Identity.ICurrentUser", IsReadOnly = true });
            deps.Add(new DependencyModel { FieldName = "_clock", TypeName = "global::Pragmatic.Temporal.Clock.IClock", IsReadOnly = true });
        }
        if (kind is "Upload" or "Download" or "DownloadThumbnail")
            deps.Add(new DependencyModel { FieldName = "_storage", TypeName = "global::Pragmatic.Storage.IFileStorage", IsReadOnly = true });

        var inputProps = ImmutableArray.CreateBuilder<ActionPropertyModel>();
        if (kind == "Upload")
        {
            inputProps.Add(new ActionPropertyModel { Name = $"{model.ParentTypeName}Id", TypeName = model.IdType, IsRequired = true });
            inputProps.Add(new ActionPropertyModel { Name = "FileContent", TypeName = "System.IO.Stream", IsRequired = true });
            inputProps.Add(new ActionPropertyModel { Name = "FileName", TypeName = "string", IsRequired = true });
            inputProps.Add(new ActionPropertyModel { Name = "FileSize", TypeName = "long", IsRequired = true });
            inputProps.Add(new ActionPropertyModel { Name = "ContentType", TypeName = "string", IsRequired = true });
            inputProps.Add(new ActionPropertyModel { Name = "Description", TypeName = "string?", IsRequired = false, IsNullable = true });
        }
        else
        {
            inputProps.Add(new ActionPropertyModel { Name = $"{model.ParentTypeName}Id", TypeName = model.IdType, IsRequired = true });
            inputProps.Add(new ActionPropertyModel { Name = "AttachmentId", TypeName = "System.Guid", IsRequired = true });
        }

        return new ActionModel
        {
            Namespace = model.ParentNamespace,
            TypeName = typeName,
            FullTypeName = $"global::{fullTypeName}",
            Accessibility = "public",
            IsVoid = isVoid,
            ReturnTypeName = returnType,
            BelongsToTypeName = boundaryKey,
            SubBoundaryName = model.SubBoundaryName,
            Dependencies = deps.ToImmutable(),
            InputProperties = inputProps.ToImmutable(),
        };
    }
}
