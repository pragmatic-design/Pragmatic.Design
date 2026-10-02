using Pragmatic.SourceGen;
using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Transforms;

/// <summary>
/// Builds <see cref="EndpointModel"/> instances for comment CRUD operations.
/// These are injected into EndpointsFeature which generates the full handler pipeline.
/// </summary>
internal static class TraitEndpointModelBuilder
{
    public static ImmutableArray<EndpointModel> BuildCommentEndpoints(CommentTraitModel model)
    {
        if (model.ResourceSegment is null) return ImmutableArray<EndpointModel>.Empty;

        var boundary = model.BoundaryName?.ToLowerInvariant() ?? "v1";
        var segment = model.ResourceSegment;
        var paramName = model.ResourceParamName ?? DeriveParamName(segment);
        var baseRoute = $"/api/{boundary}/{segment}/{{{paramName}}}/comments";
        var ns = model.ParentNamespace;
        var parent = model.ParentTypeName;

        AuthorizationModel Auth(string operation)
            => RequirePermission(TraitPermissions.Slug(model.BoundaryName, parent, TraitPermissions.CommentsGroup, operation));

        var builder = ImmutableArray.CreateBuilder<EndpointModel>();

        // POST /comments — Add
        builder.Add(new EndpointModel
        {
            Namespace = ns,
            TypeName = $"Add{parent}CommentAction",
            FullTypeName = $"global::{ns}.Add{parent}CommentAction",
            Accessibility = "public",
            HttpMethod = "Post",
            Route = baseRoute,
            IsVoid = false,
            IsDomainAction = true,
            DomainActionReturnType = "global::System.Guid",
            SuccessStatusCode = 201,
            Summary = $"Add a comment to a {parent}",
            Tags = ImmutableArray.Create("Comments"),
            Authorization = Auth(TraitPermissions.Create),
            RouteParameters = ImmutableArray.Create(new RouteParameterModel
            {
                Name = paramName,
                PropertyName = $"{model.ParentTypeName}Id",
                TypeName = model.SimpleIdType,
            }),
            BodyProperties = BuildAddBodyProperties(model),
        });

        // GET /comments — List
        builder.Add(new EndpointModel
        {
            Namespace = ns,
            TypeName = $"List{parent}CommentsQuery",
            FullTypeName = $"global::{ns}.List{parent}CommentsQuery",
            Accessibility = "public",
            HttpMethod = "Get",
            Route = baseRoute,
            IsVoid = false,
            IsDomainAction = false,
            IsQuery = true,
            QueryEntityType = $"global::{model.CommentFullTypeName}",
            QueryResultType = $"global::{ns}.{model.CommentTypeName}Dto",
            QueryBoundaryType = model.BoundaryFullTypeName is not null
                ? $"global::{model.BoundaryFullTypeName}" : null,
            QueryIsPaged = true,
            Summary = $"List comments on a {parent}",
            Tags = ImmutableArray.Create("Comments"),
            Authorization = Auth(TraitPermissions.Read),
            RouteParameters = ImmutableArray.Create(new RouteParameterModel
            {
                Name = paramName,
                PropertyName = $"{model.ParentTypeName}Id",
                TypeName = model.SimpleIdType,
            }),
        });

        // GET /comments/{commentId} — Get detail
        builder.Add(new EndpointModel
        {
            Namespace = ns,
            TypeName = $"Get{parent}CommentAction",
            FullTypeName = $"global::{ns}.Get{parent}CommentAction",
            Accessibility = "public",
            HttpMethod = "Get",
            Route = $"{baseRoute}/{{commentId}}",
            IsVoid = false,
            IsDomainAction = true,
            DomainActionReturnType = $"global::{ns}.{model.CommentTypeName}Dto",
            Summary = $"Get a single comment on a {parent}",
            Tags = ImmutableArray.Create("Comments"),
            Authorization = Auth(TraitPermissions.Read),
            RouteParameters = ImmutableArray.Create(
                new RouteParameterModel { Name = paramName, PropertyName = $"{model.ParentTypeName}Id", TypeName = model.SimpleIdType },
                new RouteParameterModel { Name = "commentId", PropertyName = "CommentId", TypeName = "Guid" }),
        });

        // PUT /comments/{commentId} — Update. Suppressed entirely when AllowEditing = false:
        // the action does not exist either (see TraitActionModelBuilder).
        if (model.AllowEditing)
        {
            builder.Add(new EndpointModel
            {
                Namespace = ns,
                TypeName = $"Update{parent}CommentAction",
                FullTypeName = $"global::{ns}.Update{parent}CommentAction",
                Accessibility = "public",
                HttpMethod = "Put",
                Route = $"{baseRoute}/{{commentId}}",
                IsVoid = true,
                IsDomainAction = true,
                IsVoidDomainAction = true,
                Summary = $"Update a comment on a {parent}",
                Tags = ImmutableArray.Create("Comments"),
                Authorization = Auth(TraitPermissions.Update),
                RouteParameters = ImmutableArray.Create(
                    new RouteParameterModel { Name = paramName, PropertyName = $"{model.ParentTypeName}Id", TypeName = model.SimpleIdType },
                    new RouteParameterModel { Name = "commentId", PropertyName = "CommentId", TypeName = "Guid" }),
                BodyProperties = ImmutableArray.Create(new BodyPropertyModel
                {
                    Name = "Content",
                    TypeName = "string",
                    IsRequired = true,
                    IsImplicit = true,
                }),
            });
        }

        // DELETE /comments/{commentId} — Delete
        builder.Add(new EndpointModel
        {
            Namespace = ns,
            TypeName = $"Delete{parent}CommentAction",
            FullTypeName = $"global::{ns}.Delete{parent}CommentAction",
            Accessibility = "public",
            HttpMethod = "Delete",
            Route = $"{baseRoute}/{{commentId}}",
            IsVoid = true,
            IsDomainAction = true,
            IsVoidDomainAction = true,
            SuccessStatusCode = 204,
            Summary = $"Delete a comment on a {parent}",
            Tags = ImmutableArray.Create("Comments"),
            Authorization = Auth(TraitPermissions.Delete),
            RouteParameters = ImmutableArray.Create(
                new RouteParameterModel { Name = paramName, PropertyName = $"{model.ParentTypeName}Id", TypeName = model.SimpleIdType },
                new RouteParameterModel { Name = "commentId", PropertyName = "CommentId", TypeName = "Guid" }),
        });

        // PUT /comments/{commentId}/moderation — Moderate. Only exists with RequireApproval, which is
        // also the only case where the action is generated. Without it the moderate permission would be
        // grantable but unreachable, and a pending comment could never be approved over HTTP.
        if (model.RequireApproval)
        {
            builder.Add(new EndpointModel
            {
                Namespace = ns,
                TypeName = $"Moderate{parent}CommentAction",
                FullTypeName = $"global::{ns}.Moderate{parent}CommentAction",
                Accessibility = "public",
                HttpMethod = "Put",
                Route = $"{baseRoute}/{{commentId}}/moderation",
                IsVoid = true,
                IsDomainAction = true,
                IsVoidDomainAction = true,
                SuccessStatusCode = 204,
                Summary = $"Approve, reject or hide a comment on a {parent}",
                Tags = ImmutableArray.Create("Comments"),
                Authorization = Auth(TraitPermissions.Moderate),
                RouteParameters = ImmutableArray.Create(
                    new RouteParameterModel { Name = paramName, PropertyName = $"{model.ParentTypeName}Id", TypeName = model.SimpleIdType },
                    new RouteParameterModel { Name = "commentId", PropertyName = "CommentId", TypeName = "Guid" }),
                BodyProperties = ImmutableArray.Create(new BodyPropertyModel
                {
                    Name = "NewStatus",
                    TypeName = "global::Pragmatic.Comments.CommentStatus",
                    IsRequired = true,
                    IsImplicit = true,
                }),
            });

            // GET /comments/pending — the moderation queue. The approve route above is only usable when
            // something lists what awaits approval; the filter that hides a pending comment from readers
            // hid it from the moderator too. A literal segment, so it wins over {commentId}.
            builder.Add(new EndpointModel
            {
                Namespace = ns,
                TypeName = $"ListPending{parent}CommentsAction",
                FullTypeName = $"global::{ns}.ListPending{parent}CommentsAction",
                Accessibility = "public",
                HttpMethod = "Get",
                Route = $"{baseRoute}/pending",
                IsVoid = false,
                IsDomainAction = true,
                DomainActionReturnType = $"global::System.Collections.Generic.IReadOnlyList<global::{ns}.{model.CommentTypeName}Dto>",
                Summary = $"List the comments on a {parent} that await approval, oldest first",
                Tags = ImmutableArray.Create("Comments"),
                Authorization = Auth(TraitPermissions.Moderate),
                RouteParameters = ImmutableArray.Create(new RouteParameterModel
                {
                    Name = paramName,
                    PropertyName = $"{model.ParentTypeName}Id",
                    TypeName = model.SimpleIdType,
                }),
                QueryParameters = ImmutableArray.Create(
                    new QueryParameterModel { Name = "page", PropertyName = "Page", TypeName = "int", IsValueType = true },
                    new QueryParameterModel { Name = "pageSize", PropertyName = "PageSize", TypeName = "int", IsValueType = true }),
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<BodyPropertyModel> BuildAddBodyProperties(CommentTraitModel model)
    {
        var props = ImmutableArray.CreateBuilder<BodyPropertyModel>();
        props.Add(new BodyPropertyModel
        {
            Name = "Content",
            TypeName = "string",
            IsRequired = true,
            IsImplicit = true,
        });
        if (model.AllowReplies)
        {
            props.Add(new BodyPropertyModel
            {
                Name = "ReplyToId",
                TypeName = "System.Guid?",
                IsRequired = false,
                IsImplicit = true,
            });
        }

        // Without this the option would be reachable only in-process: the action takes a Visibility and
        // the DTO returns one, but the HTTP body would have no field for it, so every comment posted
        // over the API would be born Public no matter how the entity is configured.
        if (model.SupportInternalNotes)
        {
            props.Add(new BodyPropertyModel
            {
                Name = "Visibility",
                TypeName = "global::Pragmatic.Comments.CommentVisibility",
                IsRequired = false,
                IsImplicit = true,
            });
        }

        return props.ToImmutable();
    }

    // ── Tag Endpoints ────────────────────────────────────────────────────

    public static ImmutableArray<EndpointModel> BuildTagEndpoints(TagTraitModel model)
    {
        if (model.ResourceSegment is null) return ImmutableArray<EndpointModel>.Empty;

        var boundary = model.BoundaryName?.ToLowerInvariant() ?? "v1";
        var segment = model.ResourceSegment;
        var paramName = model.ResourceParamName ?? DeriveParamName(segment);
        var baseRoute = $"/api/{boundary}/{segment}/{{{paramName}}}/tags";
        var ns = model.ParentNamespace;
        var parent = model.ParentTypeName;

        AuthorizationModel Auth(string operation)
            => RequirePermission(TraitPermissions.Slug(model.BoundaryName, parent, TraitPermissions.TagsGroup, operation));

        var builder = ImmutableArray.CreateBuilder<EndpointModel>();

        // POST /tags — Add tag to entity
        builder.Add(new EndpointModel
        {
            Namespace = ns,
            TypeName = $"Add{parent}TagAction",
            FullTypeName = $"global::{ns}.Add{parent}TagAction",
            Accessibility = "public",
            HttpMethod = "Post",
            Route = baseRoute,
            IsVoid = false,
            IsDomainAction = true,
            DomainActionReturnType = "global::System.Guid",
            SuccessStatusCode = 201,
            Summary = $"Add a tag to a {parent}",
            Tags = ImmutableArray.Create("Tags"),
            Authorization = Auth(TraitPermissions.Add),
            RouteParameters = ImmutableArray.Create(new RouteParameterModel
            {
                Name = paramName,
                PropertyName = $"{parent}Id",
                TypeName = model.SimpleIdType,
            }),
            BodyProperties = ImmutableArray.Create(new BodyPropertyModel
            {
                Name = "TagValue",
                TypeName = "string",
                IsRequired = true,
                IsImplicit = true,
            }),
        });

        // GET /tags — List the tags applied to the entity
        builder.Add(new EndpointModel
        {
            Namespace = ns,
            TypeName = model.ListQueryTypeName,
            FullTypeName = $"global::{ns}.{model.ListQueryTypeName}",
            Accessibility = "public",
            HttpMethod = "Get",
            Route = baseRoute,
            IsVoid = false,
            IsDomainAction = false,
            IsQuery = true,
            // The junction is the queried entity: it holds the parent FK the filter runs on.
            QueryEntityType = $"global::{model.JunctionFullTypeName}",
            QueryResultType = $"global::{ns}.{model.TagDtoTypeName}",
            QueryBoundaryType = model.BoundaryFullTypeName is not null
                ? $"global::{model.BoundaryFullTypeName}" : null,
            QueryIsPaged = true,
            Summary = $"List the tags on a {parent}",
            Tags = ImmutableArray.Create("Tags"),
            Authorization = Auth(TraitPermissions.Read),
            RouteParameters = ImmutableArray.Create(new RouteParameterModel
            {
                Name = paramName,
                PropertyName = $"{parent}Id",
                TypeName = model.SimpleIdType,
            }),
        });

        // DELETE /tags/{tagId} — Remove tag from entity
        builder.Add(new EndpointModel
        {
            Namespace = ns,
            TypeName = $"Remove{parent}TagAction",
            FullTypeName = $"global::{ns}.Remove{parent}TagAction",
            Accessibility = "public",
            HttpMethod = "Delete",
            Route = $"{baseRoute}/{{tagId}}",
            IsVoid = true,
            IsDomainAction = true,
            IsVoidDomainAction = true,
            SuccessStatusCode = 204,
            Summary = $"Remove a tag from a {parent}",
            Tags = ImmutableArray.Create("Tags"),
            Authorization = Auth(TraitPermissions.Remove),
            RouteParameters = ImmutableArray.Create(
                new RouteParameterModel { Name = paramName, PropertyName = $"{parent}Id", TypeName = model.SimpleIdType },
                new RouteParameterModel { Name = "tagId", PropertyName = "TagId", TypeName = "Guid" }),
        });

        return builder.ToImmutable();
    }

    // ── Note Endpoints ───────────────────────────────────────────────────

    public static ImmutableArray<EndpointModel> BuildNoteEndpoints(NoteTraitModel model)
    {
        if (model.ResourceSegment is null) return ImmutableArray<EndpointModel>.Empty;

        var boundary = model.BoundaryName?.ToLowerInvariant() ?? "v1";
        var segment = model.ResourceSegment;
        var paramName = model.ResourceParamName ?? DeriveParamName(segment);
        var baseRoute = $"/api/{boundary}/{segment}/{{{paramName}}}/notes";
        var ns = model.ParentNamespace;
        var parent = model.ParentTypeName;
        var dtoName = $"{model.NoteTypeName}Dto";

        AuthorizationModel Auth(string operation)
            => RequirePermission(TraitPermissions.Slug(model.BoundaryName, parent, TraitPermissions.NotesGroup, operation));

        var builder = ImmutableArray.CreateBuilder<EndpointModel>();

        // POST /notes — Add
        builder.Add(new EndpointModel
        {
            Namespace = ns, TypeName = $"Add{parent}NoteAction",
            FullTypeName = $"global::{ns}.Add{parent}NoteAction",
            Accessibility = "public", HttpMethod = "Post", Route = baseRoute,
            IsVoid = false, IsDomainAction = true,
            DomainActionReturnType = "global::System.Guid", SuccessStatusCode = 201,
            Summary = $"Add a note to a {parent}", Tags = ImmutableArray.Create("Notes"),
            Authorization = Auth(TraitPermissions.Create),
            RouteParameters = ImmutableArray.Create(new RouteParameterModel
            {
                Name = paramName, PropertyName = $"{parent}Id", TypeName = model.SimpleIdType,
            }),
            BodyProperties = ImmutableArray.Create(new BodyPropertyModel
            {
                Name = "Content", TypeName = "string", IsRequired = true, IsImplicit = true,
            }),
        });

        // GET /notes — List
        builder.Add(new EndpointModel
        {
            Namespace = ns, TypeName = $"List{parent}NotesQuery",
            FullTypeName = $"global::{ns}.List{parent}NotesQuery",
            Accessibility = "public", HttpMethod = "Get", Route = baseRoute,
            IsVoid = false, IsDomainAction = false, IsQuery = true,
            QueryEntityType = $"global::{model.NoteFullTypeName}",
            QueryResultType = $"global::{ns}.{dtoName}",
            QueryBoundaryType = model.BoundaryFullTypeName is not null ? $"global::{model.BoundaryFullTypeName}" : null,
            QueryIsPaged = true,
            Summary = $"List notes on a {parent}", Tags = ImmutableArray.Create("Notes"),
            Authorization = Auth(TraitPermissions.Read),
            RouteParameters = ImmutableArray.Create(new RouteParameterModel
            {
                Name = paramName, PropertyName = $"{parent}Id", TypeName = model.SimpleIdType,
            }),
        });

        // GET /notes/{noteId} — Get detail
        builder.Add(new EndpointModel
        {
            Namespace = ns, TypeName = $"Get{parent}NoteAction",
            FullTypeName = $"global::{ns}.Get{parent}NoteAction",
            Accessibility = "public", HttpMethod = "Get", Route = $"{baseRoute}/{{noteId}}",
            IsVoid = false, IsDomainAction = true,
            DomainActionReturnType = $"global::{ns}.{dtoName}",
            Summary = $"Get a note on a {parent}", Tags = ImmutableArray.Create("Notes"),
            Authorization = Auth(TraitPermissions.Read),
            RouteParameters = ImmutableArray.Create(
                new RouteParameterModel { Name = paramName, PropertyName = $"{parent}Id", TypeName = model.SimpleIdType },
                new RouteParameterModel { Name = "noteId", PropertyName = "NoteId", TypeName = "Guid" }),
        });

        // PUT /notes/{noteId} — Update. Suppressed entirely when AllowEditing = false:
        // the action does not exist either (see TraitActionModelBuilder).
        if (model.AllowEditing)
        {
            builder.Add(new EndpointModel
            {
                Namespace = ns, TypeName = $"Update{parent}NoteAction",
                FullTypeName = $"global::{ns}.Update{parent}NoteAction",
                Accessibility = "public", HttpMethod = "Put", Route = $"{baseRoute}/{{noteId}}",
                IsVoid = true, IsDomainAction = true, IsVoidDomainAction = true,
                Summary = $"Update a note on a {parent}", Tags = ImmutableArray.Create("Notes"),
                Authorization = Auth(TraitPermissions.Update),
                RouteParameters = ImmutableArray.Create(
                    new RouteParameterModel { Name = paramName, PropertyName = $"{parent}Id", TypeName = model.SimpleIdType },
                    new RouteParameterModel { Name = "noteId", PropertyName = "NoteId", TypeName = "Guid" }),
                BodyProperties = ImmutableArray.Create(new BodyPropertyModel
                {
                    Name = "Content", TypeName = "string", IsRequired = true, IsImplicit = true,
                }),
            });
        }

        // DELETE /notes/{noteId} — Delete
        builder.Add(new EndpointModel
        {
            Namespace = ns, TypeName = $"Delete{parent}NoteAction",
            FullTypeName = $"global::{ns}.Delete{parent}NoteAction",
            Accessibility = "public", HttpMethod = "Delete", Route = $"{baseRoute}/{{noteId}}",
            IsVoid = true, IsDomainAction = true, IsVoidDomainAction = true,
            SuccessStatusCode = 204,
            Summary = $"Delete a note on a {parent}", Tags = ImmutableArray.Create("Notes"),
            Authorization = Auth(TraitPermissions.Delete),
            RouteParameters = ImmutableArray.Create(
                new RouteParameterModel { Name = paramName, PropertyName = $"{parent}Id", TypeName = model.SimpleIdType },
                new RouteParameterModel { Name = "noteId", PropertyName = "NoteId", TypeName = "Guid" }),
        });

        return builder.ToImmutable();
    }

    // ── Attachment Endpoints ──────────────────────────────────────────

    public static ImmutableArray<EndpointModel> BuildAttachmentEndpoints(AttachmentTraitModel model, bool imagingAvailable = false)
    {
        if (model.ResourceSegment is null) return ImmutableArray<EndpointModel>.Empty;

        var boundary = model.BoundaryName?.ToLowerInvariant() ?? "v1";
        var segment = model.ResourceSegment;
        var paramName = model.ResourceParamName ?? DeriveParamName(segment);
        var baseRoute = $"/api/{boundary}/{segment}/{{{paramName}}}/attachments";
        var ns = model.ParentNamespace;
        var parent = model.ParentTypeName;
        var dtoName = $"{model.AttachmentTypeName}Dto";

        AuthorizationModel Auth(string operation)
            => RequirePermission(TraitPermissions.Slug(model.BoundaryName, parent, TraitPermissions.AttachmentsGroup, operation));

        var builder = ImmutableArray.CreateBuilder<EndpointModel>();

        // POST /attachments — Upload (multipart/form-data). The handler reads an IFormFile and maps
        // it onto the action's FileContent/FileName/FileSize/ContentType members (see AttachmentUpload).
        var allowedExts = string.IsNullOrEmpty(model.AllowedExtensions)
            ? ImmutableArray<string>.Empty
            : model.AllowedExtensions
                .Split(',')
                .Select(e =>
                {
                    var t = e.Trim().ToLowerInvariant();
                    return t.StartsWith(".") || t.Length == 0 ? t : "." + t;
                })
                .Where(e => e.Length > 0)
                .ToImmutableArray();

        builder.Add(new EndpointModel
        {
            Namespace = ns,
            TypeName = $"Upload{parent}AttachmentAction",
            FullTypeName = $"global::{ns}.Upload{parent}AttachmentAction",
            Accessibility = "public",
            HttpMethod = "Post",
            Route = baseRoute,
            IsVoid = false,
            IsDomainAction = true,
            DomainActionReturnType = "global::System.Guid",
            SuccessStatusCode = 201,
            Summary = $"Upload an attachment to a {parent}",
            Tags = ImmutableArray.Create("Attachments"),
            Authorization = Auth(TraitPermissions.Upload),
            RouteParameters = ImmutableArray.Create(new RouteParameterModel
            {
                Name = paramName,
                PropertyName = $"{parent}Id",
                TypeName = model.SimpleIdType,
            }),
            AttachmentUpload = new AttachmentUploadModel
            {
                FileContentProperty = "FileContent",
                FileNameProperty = "FileName",
                FileSizeProperty = "FileSize",
                ContentTypeProperty = "ContentType",
                DescriptionProperty = "Description",
                MaxFileSizeBytes = model.MaxFileSizeBytes,
                AllowedExtensions = allowedExts,
            },
        });

        // GET /attachments — List
        builder.Add(new EndpointModel
        {
            Namespace = ns, TypeName = $"List{parent}AttachmentsQuery",
            FullTypeName = $"global::{ns}.List{parent}AttachmentsQuery",
            Accessibility = "public", HttpMethod = "Get", Route = baseRoute,
            IsVoid = false, IsDomainAction = false, IsQuery = true,
            QueryEntityType = $"global::{model.AttachmentFullTypeName}",
            QueryResultType = $"global::{ns}.{dtoName}",
            QueryBoundaryType = model.BoundaryFullTypeName is not null ? $"global::{model.BoundaryFullTypeName}" : null,
            QueryIsPaged = true,
            Summary = $"List attachments on a {parent}", Tags = ImmutableArray.Create("Attachments"),
            Authorization = Auth(TraitPermissions.Read),
            RouteParameters = ImmutableArray.Create(new RouteParameterModel
            {
                Name = paramName, PropertyName = $"{parent}Id", TypeName = model.SimpleIdType,
            }),
        });

        // GET /attachments/{attachmentId} — Get detail
        builder.Add(new EndpointModel
        {
            Namespace = ns, TypeName = $"Get{parent}AttachmentAction",
            FullTypeName = $"global::{ns}.Get{parent}AttachmentAction",
            Accessibility = "public", HttpMethod = "Get", Route = $"{baseRoute}/{{attachmentId}}",
            IsVoid = false, IsDomainAction = true,
            DomainActionReturnType = $"global::{ns}.{dtoName}",
            Summary = $"Get attachment details on a {parent}", Tags = ImmutableArray.Create("Attachments"),
            Authorization = Auth(TraitPermissions.Read),
            RouteParameters = ImmutableArray.Create(
                new RouteParameterModel { Name = paramName, PropertyName = $"{parent}Id", TypeName = model.SimpleIdType },
                new RouteParameterModel { Name = "attachmentId", PropertyName = "AttachmentId", TypeName = "Guid" }),
        });

        // GET /attachments/{attachmentId}/content — Download the bytes.
        // Separate route from the metadata GET so the two responses stay distinguishable (JSON vs
        // file) without content negotiation, and so the file URL can be linked/downloaded directly.
        // Same permission as the metadata read: exposing the content under a weaker gate than the
        // metadata that describes it would be backwards.
        builder.Add(new EndpointModel
        {
            Namespace = ns, TypeName = $"Download{parent}AttachmentAction",
            FullTypeName = $"global::{ns}.Download{parent}AttachmentAction",
            Accessibility = "public", HttpMethod = "Get",
            Route = $"{baseRoute}/{{attachmentId}}/content",
            IsVoid = false, IsDomainAction = true,
            DomainActionReturnType = "global::Pragmatic.Endpoints.Responses.FileResponse",
            Summary = $"Download the content of an attachment on a {parent}",
            Tags = ImmutableArray.Create("Attachments"),
            Authorization = Auth(TraitPermissions.Read),
            RouteParameters = ImmutableArray.Create(
                new RouteParameterModel { Name = paramName, PropertyName = $"{parent}Id", TypeName = model.SimpleIdType },
                new RouteParameterModel { Name = "attachmentId", PropertyName = "AttachmentId", TypeName = "Guid" }),
        });

        // GET /attachments/{attachmentId}/thumbnail — the derived preview, when there is one.
        // Only when the derivation is actually emitted: a route that can only ever answer 404 is a
        // declared capability that does nothing, which is what this whole feature exists to avoid.
        // Same permission as the content it is derived from — a preview of a file is the file.
        if (imagingAvailable && model.ThumbnailRequested)
        {
            builder.Add(new EndpointModel
            {
                Namespace = ns, TypeName = $"DownloadThumbnail{parent}AttachmentAction",
                FullTypeName = $"global::{ns}.DownloadThumbnail{parent}AttachmentAction",
                Accessibility = "public", HttpMethod = "Get",
                Route = $"{baseRoute}/{{attachmentId}}/thumbnail",
                IsVoid = false, IsDomainAction = true,
                DomainActionReturnType = "global::Pragmatic.Endpoints.Responses.FileResponse",
                Summary = $"Download the thumbnail of an attachment on a {parent}",
                Tags = ImmutableArray.Create("Attachments"),
                Authorization = Auth(TraitPermissions.Read),
                RouteParameters = ImmutableArray.Create(
                    new RouteParameterModel { Name = paramName, PropertyName = $"{parent}Id", TypeName = model.SimpleIdType },
                    new RouteParameterModel { Name = "attachmentId", PropertyName = "AttachmentId", TypeName = "Guid" }),
            });
        }

        // DELETE /attachments/{attachmentId} — Delete
        builder.Add(new EndpointModel
        {
            Namespace = ns, TypeName = $"Delete{parent}AttachmentAction",
            FullTypeName = $"global::{ns}.Delete{parent}AttachmentAction",
            Accessibility = "public", HttpMethod = "Delete", Route = $"{baseRoute}/{{attachmentId}}",
            IsVoid = true, IsDomainAction = true, IsVoidDomainAction = true,
            SuccessStatusCode = 204,
            Summary = $"Delete an attachment on a {parent}", Tags = ImmutableArray.Create("Attachments"),
            Authorization = Auth(TraitPermissions.Delete),
            RouteParameters = ImmutableArray.Create(
                new RouteParameterModel { Name = paramName, PropertyName = $"{parent}Id", TypeName = model.SimpleIdType },
                new RouteParameterModel { Name = "attachmentId", PropertyName = "AttachmentId", TypeName = "Guid" }),
        });

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Trait endpoints are generated, so nobody can annotate them with [RequirePermission];
    ///     without this the generated CRUD would be reachable by any authenticated user.
    /// </summary>
    private static AuthorizationModel RequirePermission(string permission)
        => new()
        {
            IsRequired = true,
            RequiredPermissions = ImmutableArray.Create(permission),
        };

    private static string DeriveParamName(string segment)
        => StringHelper.ToCamelCaseIdentifier(StringHelper.Singularize(segment)) + "Id";
}
