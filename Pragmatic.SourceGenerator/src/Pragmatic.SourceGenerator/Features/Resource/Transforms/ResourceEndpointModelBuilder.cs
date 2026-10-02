using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Resource.Models;

namespace Pragmatic.SourceGenerator.Features.Resource.Transforms;

/// <summary>
/// Builds <see cref="EndpointModel"/> instances for Resource CRUD operations.
/// Injected into EndpointsFeature for full handler pipeline generation.
/// </summary>
internal static class ResourceEndpointModelBuilder
{
    // ResourceCapabilities flags
    private const int CapCreate = 1;
    private const int CapRead = 2;
    private const int CapUpdate = 4;
    private const int CapDelete = 8;
    private const int CapList = 16;
    private const int CapSearch = 32;
    private const int CapRestore = 64;

    public static ImmutableArray<EndpointModel> Build(ResourceCrudModel model)
    {
        var caps = model.Resource.Capabilities;
        var builder = ImmutableArray.CreateBuilder<EndpointModel>();

        var boundary = model.Resource.BoundaryName?.ToLowerInvariant() ?? "v1";
        var segment = model.Resource.Segment;
        var baseRoute = $"/api/{boundary}/{segment}";
        var ns = model.Resource.Namespace;
        var entity = model.Resource.TypeName;

        // Route parameters bind by simple name inside the generated file's namespace…
        var idType = model.Resource.IdType.Contains('.')
            ? model.Resource.IdType.Substring(model.Resource.IdType.LastIndexOf('.') + 1)
            : model.Resource.IdType;

        // …but the action's return type must be fully qualified. Never assume the id lives in
        // System: a strongly-typed id (ReservationId) is declared by the consumer.
        var qualifiedIdType = $"global::{model.Resource.IdType}";
        var qualifiedEntityType = model.Resource.FullTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? model.Resource.FullTypeName
            : $"global::{model.Resource.FullTypeName}";
        var paramName = model.Resource.ResolvedParamName;

        // The permission is enforced from the endpoint's AuthorizationModel, not from the mutation's.
        // Setting it only on the MutationModel left the generated handler mapped with no authorization
        // at all and the build green — the shape this repository keeps meeting: a correct value nobody
        // reads. Verified by grepping the generated endpoint for "permission" and finding nothing.
        AuthorizationModel Requires(string verb) => new()
        {
            IsRequired = true,
            RequiredPermissions = new EquatableArray<string>(ImmutableArray.Create(
                PermissionNaming.ValueForEntityMember(
                    model.Resource.BoundaryName?.ToLowerInvariant(), model.Resource.TypeName, verb))),
        };

        if ((caps & CapCreate) != 0)
        {
            var dto = model.DtoFor($"ResourceCreate{entity}Mutation", model.ScaffoldedReadDto);
            builder.Add(new EndpointModel
            {
                Namespace = ns,
                TypeName = $"ResourceCreate{entity}Mutation",
                FullTypeName = $"global::{ns}.ResourceCreate{entity}Mutation",
                Accessibility = "public",
                HttpMethod = "Post",
                Route = baseRoute,
                IsVoid = false,
                IsDomainAction = false,
                IsMutation = true,
                MutationEntityType = qualifiedEntityType,
                MutationModeName = "Create",
                MutationCanConflict = model.CanConflict,
                // What the invoker works with stays the entity; what goes on the wire is the same DTO
                // the read answers with. Both are declared: MutationEntityType types the invoker,
                // ResponseType is what the manifest, the OpenAPI document, the generated client and the
                // JSON context all read — and it comes first in that chain, so there is nothing to
                // disagree about.
                MutationResponseFactory = $"{dto.Qualified}.FromEntity",
                ResponseType = dto.Qualified,
                Authorization = Requires("create"),
                SuccessStatusCode = 201,
                Summary = $"Create a new {entity}",
                Tags = ImmutableArray.Create(entity),
                BodyProperties = BuildCreateBodyProperties(model),
                HintPrefix = $"_Resource.{entity}",
                HintSuffix = "Create",
            });
        }

        if ((caps & CapRead) != 0)
        {
            builder.Add(new EndpointModel
            {
                Namespace = ns,
                TypeName = $"ResourceRead{entity}Query",
                FullTypeName = $"global::{ns}.ResourceRead{entity}Query",
                Accessibility = "public",
                HttpMethod = "Get",
                Route = $"{baseRoute}/{{{paramName}}}",
                IsVoid = false,
                IsDomainAction = false,
                IsQuery = true,
                QueryIsSingle = true,
                QueryEntityType = qualifiedEntityType,
                QueryResultType = model.DtoFor($"ResourceRead{entity}Query", model.ScaffoldedReadDto).Qualified,
                QueryBoundaryType = model.Resource.BoundaryFullTypeName,
                Authorization = Requires("read"),
                Summary = $"Get a {entity} by id",
                Tags = ImmutableArray.Create(entity),
                HintPrefix = $"_Resource.{entity}",
                HintSuffix = "Read",
                RouteParameters = ImmutableArray.Create(new RouteParameterModel
                {
                    Name = paramName,
                    PropertyName = "Id",
                    TypeName = idType,
                }),
            });

            // Read by LogicKey (if entity has [GeneratedValue])
            if (model.LogicKeyName is not null)
            {
                var lkParam = ToCamelCase(model.LogicKeyName);
                builder.Add(new EndpointModel
                {
                    Namespace = ns,
                    TypeName = $"ResourceReadBy{model.LogicKeyName}{entity}Query",
                    FullTypeName = $"global::{ns}.ResourceReadBy{model.LogicKeyName}{entity}Query",
                    Accessibility = "public",
                    HttpMethod = "Get",
                    Route = $"{baseRoute}/by-{ToKebab(model.LogicKeyName)}/{{{lkParam}}}",
                    IsVoid = false,
                    IsDomainAction = false,
                    IsQuery = true,
                    QueryIsSingle = true,
                    QueryEntityType = qualifiedEntityType,
                    QueryResultType = model
                        .DtoFor($"ResourceReadBy{model.LogicKeyName}{entity}Query", model.ScaffoldedReadDto)
                        .Qualified,
                    QueryBoundaryType = model.Resource.BoundaryFullTypeName,
                    Authorization = Requires("read"),
                    Summary = $"Get a {entity} by {model.LogicKeyName}",
                    Tags = ImmutableArray.Create(entity),
                    HintPrefix = $"_Resource.{entity}",
                    HintSuffix = $"ReadBy{model.LogicKeyName}",
                    RouteParameters = ImmutableArray.Create(new RouteParameterModel
                    {
                        Name = lkParam,
                        PropertyName = model.LogicKeyName,
                        TypeName = model.LogicKeyType ?? "string",
                    }),
                });
            }
        }

        if ((caps & CapUpdate) != 0)
        {
            var dto = model.DtoFor($"ResourceUpdate{entity}Mutation", model.ScaffoldedReadDto);
            builder.Add(new EndpointModel
            {
                Namespace = ns,
                TypeName = $"ResourceUpdate{entity}Mutation",
                FullTypeName = $"global::{ns}.ResourceUpdate{entity}Mutation",
                Accessibility = "public",
                HttpMethod = "Put",
                Route = $"{baseRoute}/{{{paramName}}}",
                IsVoid = false,
                IsDomainAction = false,
                IsMutation = true,
                MutationEntityType = qualifiedEntityType,
                MutationModeName = "Update",
                MutationCanConflict = model.CanConflict,
                // The resource still exists after the operation, so the operation answers with it —
                // the same shape the read answers with. Create answering with the resource and Update
                // answering with nothing was not a decision, it was two defaults meeting.
                MutationResponseFactory = $"{dto.Qualified}.FromEntity",
                ResponseType = dto.Qualified,
                SuccessStatusCode = 200,
                Authorization = Requires("update"),
                Summary = $"Update a {entity}",
                Tags = ImmutableArray.Create(entity),
                HintPrefix = $"_Resource.{entity}",
                HintSuffix = "Update",
                RouteParameters = ImmutableArray.Create(new RouteParameterModel
                {
                    Name = paramName,
                    PropertyName = "Id",
                    TypeName = idType,
                }),
                BodyProperties = BuildUpdateBodyProperties(model),
            });
        }

        if ((caps & CapDelete) != 0)
        {
            // A soft delete leaves the resource there, marked; a hard one does not. So the first
            // answers with it and the second answers with nothing — the rule being that a write
            // answers with the read shape when the resource still exists afterwards, and 204 when it
            // does not.
            var dto = model.DtoFor($"ResourceDelete{entity}Mutation", model.ScaffoldedReadDto);
            builder.Add(new EndpointModel
            {
                Namespace = ns,
                TypeName = $"ResourceDelete{entity}Mutation",
                FullTypeName = $"global::{ns}.ResourceDelete{entity}Mutation",
                Accessibility = "public",
                HttpMethod = "Delete",
                Route = $"{baseRoute}/{{{paramName}}}",
                IsVoid = !model.IsSoftDelete,
                IsDomainAction = false,
                IsMutation = true,
                MutationEntityType = qualifiedEntityType,
                MutationModeName = "Delete",
                MutationCanConflict = model.CanConflict,
                MutationResponseFactory = model.IsSoftDelete ? $"{dto.Qualified}.FromEntity" : null,
                ResponseType = model.IsSoftDelete ? dto.Qualified : null,
                Authorization = Requires("delete"),
                SuccessStatusCode = model.IsSoftDelete ? 200 : 204,
                Summary = $"Delete a {entity}",
                Tags = ImmutableArray.Create(entity),
                HintPrefix = $"_Resource.{entity}",
                HintSuffix = "Delete",
                RouteParameters = ImmutableArray.Create(new RouteParameterModel
                {
                    Name = paramName,
                    PropertyName = "Id",
                    TypeName = idType,
                }),
            });
        }

        // Restore is a POST on a sub-route rather than a second DELETE: the verb has to differ from
        // the delete it undoes, and the row is addressed by the same id.
        if ((caps & CapRestore) != 0 && model.IsSoftDelete)
        {
            var dto = model.DtoFor($"ResourceRestore{entity}Mutation", model.ScaffoldedReadDto);
            builder.Add(new EndpointModel
            {
                Namespace = ns,
                TypeName = $"ResourceRestore{entity}Mutation",
                FullTypeName = $"global::{ns}.ResourceRestore{entity}Mutation",
                Accessibility = "public",
                HttpMethod = "Post",
                Route = $"{baseRoute}/{{{paramName}}}/restore",
                IsVoid = false,
                IsDomainAction = false,
                IsMutation = true,
                MutationEntityType = qualifiedEntityType,
                MutationModeName = "Restore",
                MutationCanConflict = model.CanConflict,
                MutationResponseFactory = $"{dto.Qualified}.FromEntity",
                ResponseType = dto.Qualified,
                // Whoever may delete may put it back. A verb of its own would need a constant nothing
                // emits, and an unresolvable permission is one that is never enforced.
                Authorization = Requires("delete"),
                SuccessStatusCode = 200,
                Summary = $"Restore a soft-deleted {entity}",
                Tags = ImmutableArray.Create(entity),
                HintPrefix = $"_Resource.{entity}",
                HintSuffix = "Restore",
                RouteParameters = ImmutableArray.Create(new RouteParameterModel
                {
                    Name = paramName,
                    PropertyName = "Id",
                    TypeName = idType,
                }),
            });
        }

        if ((caps & CapList) != 0)
        {
            builder.Add(new EndpointModel
            {
                Namespace = ns,
                TypeName = $"ResourceList{entity}Query",
                FullTypeName = $"global::{ns}.ResourceList{entity}Query",
                Accessibility = "public",
                HttpMethod = "Get",
                Route = baseRoute,
                IsVoid = false,
                IsDomainAction = false,
                IsQuery = true,
                QueryEntityType = $"global::{ns}.{entity}",
                QueryResultType = model.DtoFor($"ResourceList{entity}Query", model.ScaffoldedListItemDto).Qualified,
                ResponseType = Paged(model.DtoFor($"ResourceList{entity}Query", model.ScaffoldedListItemDto)),
                QueryBoundaryType = model.Resource.BoundaryFullTypeName,
                QueryIsPaged = true,
                // The same permission the single read requires. Listing every row of a resource is not
                // a lesser thing than reading one of them, but this endpoint asked for nothing at all
                // while GET /{id} next to it asked for booking.{entity}.read — so the way to read the
                // whole table was to not ask for one row. Tightened deliberately; a caller who could
                // list before and cannot now was never meant to.
                Authorization = Requires("read"),
                Summary = $"List {entity} entities",
                Tags = ImmutableArray.Create(entity),
                HintPrefix = $"_Resource.{entity}",
                HintSuffix = "List",
            });
        }

        if ((caps & CapSearch) != 0)
        {
            builder.Add(new EndpointModel
            {
                Namespace = ns,
                TypeName = $"ResourceSearch{entity}Query",
                FullTypeName = $"global::{ns}.ResourceSearch{entity}Query",
                Accessibility = "public",
                HttpMethod = "Get",
                Route = $"{baseRoute}/search",
                IsVoid = false,
                IsDomainAction = false,
                IsQuery = true,
                QueryEntityType = $"global::{ns}.{entity}",
                QueryResultType = model.DtoFor($"ResourceSearch{entity}Query", model.ScaffoldedListItemDto).Qualified,
                ResponseType = Paged(model.DtoFor($"ResourceSearch{entity}Query", model.ScaffoldedListItemDto)),
                QueryBoundaryType = model.Resource.BoundaryFullTypeName,
                QueryIsPaged = true,
                Authorization = Requires("read"),
                // Without these the search would have its filters and no way to be given any: the query
                // type declares them, an endpoint binding page and pageSize only would ignore them, and
                // every request would return the whole table.
                QueryParameters = SearchQueryParameters(model),
                Summary = $"Search {entity} entities by text fields",
                Tags = ImmutableArray.Create(entity),
                HintPrefix = $"_Resource.{entity}",
                HintSuffix = "Search",
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     What a paged query puts on the wire: a page of the DTO, not one of them.
    /// </summary>
    /// <remarks>
    ///     The scaffolded list and search left <c>ResponseType</c> unset, so the manifest fell back to
    ///     <c>QueryResultType</c> — the item type. The document, the OpenAPI schema and the generated
    ///     typed client therefore all described a page as a single item:
    ///     <c>Task&lt;Result&lt;GuestListItemDto, IError&gt;&gt;</c> for an endpoint that answers
    ///     <c>{ items, page, pageSize, total }</c>. A hand-written query sets the wrapper itself, which
    ///     is why only the scaffolded ones were wrong.
    /// </remarks>
    private static string Paged(ResourceDtoRef dto)
        => $"global::Pragmatic.Persistence.Query.Results.PagedResult<{dto.Qualified}>";

    /// <summary>
    ///     The filters of the scaffolded search, as query-string parameters.
    /// </summary>
    /// <remarks>
    ///     Read from the same model the query template renders its properties from, so the two cannot
    ///     name different sets. A filter the endpoint does not bind is a property nobody can set.
    /// </remarks>
    private static ImmutableArray<QueryParameterModel> SearchQueryParameters(ResourceCrudModel model)
    {
        var parameters = ImmutableArray.CreateBuilder<QueryParameterModel>();

        foreach (var filter in model.SearchFilters)
        {
            // Paging is bound by the endpoint template from QueryIsPaged, and a sort is not something a
            // caller sets by name here.
            if (filter.IsPageProperty || filter.IsPageSizeProperty || filter.IsSort)
                continue;

            parameters.Add(new QueryParameterModel
            {
                Name = ToCamelCase(filter.PropertyName),
                PropertyName = filter.PropertyName,
                TypeName = filter.PropertyType,
                IsRequired = filter.IsRequired,
            });
        }

        return parameters.ToImmutable();
    }

    private static ImmutableArray<BodyPropertyModel> BuildCreateBodyProperties(ResourceCrudModel model)
    {
        var props = ImmutableArray.CreateBuilder<BodyPropertyModel>();
        foreach (var p in model.CreateProperties)
        {
            props.Add(new BodyPropertyModel
            {
                Name = p.Name,
                TypeName = p.TypeName,
                IsRequired = p.IsRequired,
                IsImplicit = true,
                IsScalar = IsScalarType(p.TypeName),
            });
        }
        return props.ToImmutable();
    }

    private static ImmutableArray<BodyPropertyModel> BuildUpdateBodyProperties(ResourceCrudModel model)
    {
        var props = ImmutableArray.CreateBuilder<BodyPropertyModel>();
        foreach (var p in model.UpdateProperties)
        {
            var typeName = p.IsNullable ? p.TypeName : $"{p.TypeName}?";
            props.Add(new BodyPropertyModel
            {
                Name = p.Name,
                TypeName = typeName,
                IsRequired = false,
                IsNullable = true,
                IsImplicit = true,
                IsScalar = IsScalarType(p.TypeName),
            });
        }
        return props.ToImmutable();
    }

    private static string ToCamelCase(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    private static string ToKebab(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) sb.Append('-');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    private static bool IsScalarType(string typeName)
    {
        var clean = typeName.TrimEnd('?');
        return clean is "string" or "Guid" or "int" or "long" or "decimal" or "double"
            or "float" or "bool" or "DateTimeOffset" or "DateTime";
    }
}
