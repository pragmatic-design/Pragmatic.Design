using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Manifest.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Manifest.Transforms;

/// <summary>
///     Aggregates data from endpoint, entity, and action models into a unified ManifestModel.
/// </summary>
internal static class ManifestTransform
{
    public static ManifestModel Build(
        string assemblyName,
        ImmutableArray<EndpointModel> endpoints,
        ImmutableArray<EntityMetadataModel> entities,
        Compilation? compilation,
        ImmutableArray<ManifestTypeModel> contributedTypes = default,
        ImmutableArray<Core.DerivedPermissionEntry> derivedPermissions = default)
    {
        var validEndpoints = endpoints.Where(e => e.IsValid).ToImmutableArray();

        // Keyed the same way ExtractPermissions matches them: the endpoint's FullTypeName IS the
        // operation's, minus the global:: prefix.
        var derivedByOperation = IndexDerivedPermissions(derivedPermissions);

        var manifestEndpoints = validEndpoints
            .Select(e => TransformEndpoint(e, compilation, derivedByOperation))
            .ToImmutableArray();

        // Types the generator creates itself cannot be resolved from the compilation — it is the one
        // creating them — so the features that generate them contribute their descriptions directly.
        var types = MergeTypes(BuildTypes(entities, validEndpoints, compilation), contributedTypes, compilation);
        var boundaries = BuildBoundaries(entities);
        var permissions = ExtractPermissions(validEndpoints, derivedPermissions);
        var actions = ExtractActions(validEndpoints);

        // Every catalogue in this document is ordered by the name a reader binds to, because the
        // document is published and diffed and its order is a property of the document rather than of
        // the order the pipeline handed things over. Measured on a pure relocation of five files
        // between folders: the sets were unchanged and endpoints, types and permissions all came back
        // in a different order, which costs a reader the ability to tell a rename from a change.
        // ⚠️ Not everything: a route's `parameters` are positional in the source and stay as declared.
        return new ManifestModel
        {
            Assembly = assemblyName,
            SchemaVersion = "1.0.0",
            Boundaries = boundaries,
            Endpoints = [.. manifestEndpoints.OrderBy(e => e.OperationId, StringComparer.Ordinal)],
            Types = [.. types.OrderBy(t => t.Type, StringComparer.Ordinal)],
            Actions = [.. actions.OrderBy(a => a.Type, StringComparer.Ordinal)],
            Permissions = permissions
        };
    }

    /// <summary>
    ///     Adds the contributed descriptions, keeping whatever the compilation already resolved: a real
    ///     symbol carries more (validation attributes, precision, summaries) than a contributed shape.
    /// </summary>
    private static ImmutableArray<ManifestTypeModel> MergeTypes(
        ImmutableArray<ManifestTypeModel> discovered,
        ImmutableArray<ManifestTypeModel> contributed,
        Compilation? compilation)
    {
        if (contributed.IsDefaultOrEmpty)
            return discovered;

        var known = new HashSet<string>(discovered.Select(t => t.SimpleName), StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<ManifestTypeModel>();
        builder.AddRange(discovered);

        foreach (var type in contributed)
        {
            if (known.Add(type.SimpleName))
                builder.Add(type);
        }

        // An enum a contributed DTO refers to is a real type, only nobody walked to it: the discovery pass
        // starts from resolved symbols, and a contributed DTO is not one. Resolve them here so the members
        // come from the symbol rather than from a second copy of the list.
        if (compilation is not null)
        {
            foreach (var type in contributed)
            {
                foreach (var property in type.Properties)
                {
                    if (!property.IsEnum) continue;
                    // No index-from-end here: System.Index does not exist in netstandard2.0 (docs/CONVENTIONS.md).
                    var parts = property.Type.Replace("global::", "").Split('.');
                    var simpleName = parts[parts.Length - 1];
                    if (!known.Add(simpleName)) continue;
                    DiscoverTypeFromCompilation(property.Type, compilation, new HashSet<string>(), builder);
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The type a non-void endpoint returns, taken from whichever field carries it.
    ///     <para>
    ///         An endpoint written by hand fills <c>ResponseType</c>. One that a feature *generates* — a trait's
    ///         CRUD, a <c>[Resource]</c>, an <c>[Autocomplete]</c> — is built as a model rather than parsed from
    ///         source, and states its result in <c>QueryResultType</c> or <c>DomainActionReturnType</c> instead.
    ///         Reading only <c>ResponseType</c> left those endpoints declaring <c>isVoid: false</c> with no
    ///         response at all, and a generated client could only expose them as <c>object</c>.
    ///     </para>
    ///     <para>
    ///         <c>MutationEntityType</c> is last because a mutation endpoint answers with the entity the
    ///         invoker returns. A hand-written one fills <c>ResponseType</c> as well, so this only ever
    ///         speaks for a generated mutation — and without it <c>[Resource]</c>'s create was the one
    ///         endpoint in the Showcase to raise PRAG2303 and reach the typed client as <c>object</c>.
    ///     </para>
    /// </summary>
    private static ManifestTypeRef? BuildResponseType(EndpointModel ep)
    {
        if (ep.IsVoid)
            return null;

        var type = ep.ResponseType ?? ep.QueryResultType ?? ep.DomainActionReturnType ?? ep.MutationEntityType;
        if (string.IsNullOrEmpty(type))
            return null;

        return new ManifestTypeRef
        {
            Type = type!,
            IsPaged = ep.QueryIsPaged,
            IsStream = ep.IsStreamingResponse
        };
    }

    /// <summary>
    ///     Derived permissions by the operation type they were derived for. Empty when auto-derivation is
    ///     off, which is its default.
    /// </summary>
    /// <remarks>
    ///     One operation can carry more than one entry — auto-derivation and an explicit contribution both
    ///     land here — so this maps to a list. Keying to a single value would drop all but the last, and
    ///     the endpoint would publish a requirement narrower than the one it enforces.
    /// </remarks>
    private static Dictionary<string, ImmutableArray<string>> IndexDerivedPermissions(
        ImmutableArray<Core.DerivedPermissionEntry> derivedPermissions)
    {
        var index = new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal);
        if (derivedPermissions.IsDefaultOrEmpty)
            return index;

        foreach (var entry in derivedPermissions)
            index[entry.OperationTypeFqn] = index.TryGetValue(entry.OperationTypeFqn, out var existing)
                ? existing.Add(entry.Name)
                : ImmutableArray.Create(entry.Name);

        return index;
    }

    private static ManifestEndpointModel TransformEndpoint(
        EndpointModel ep, Compilation? compilation, Dictionary<string, ImmutableArray<string>> derivedByOperation)
    {
        // Resolve full route from group prefix + endpoint route
        var groupPrefix = ResolveGroupPrefix(ep.Group);
        var route = ep.Route ?? "";
        var fullRoute = string.IsNullOrEmpty(groupPrefix)
            ? "/" + route.TrimStart('/')
            : groupPrefix.TrimEnd('/') + "/" + route.TrimStart('/');

        // Derive boundary name from group tag or namespace
        var boundaryName = ep.Group?.Tag ?? DeriveFromNamespace(ep.Namespace);

        var operationId = !string.IsNullOrEmpty(boundaryName)
            ? $"{boundaryName}.{ep.Name ?? ep.TypeName}"
            : ep.Name ?? ep.TypeName;

        return new ManifestEndpointModel
        {
            OperationId = operationId,
            HttpMethod = ep.HttpMethod ?? "GET",
            FullRoute = fullRoute,
            Summary = ep.Summary,
            Description = ep.Description,
            Tags = ep.Tags,
            // The computed one, not the declared one. `?? 200` published 200 for every create — the
            // endpoint answers 201, the runtime document said 201, and the manifest that feeds the
            // shipped document said otherwise.
            SuccessStatusCode = ep.ComputedSuccessStatusCode,
            IsVoid = ep.IsVoid,
            ResponseType = BuildResponseType(ep),
            Parameters = TransformParameters(ep),
            RequestBody = TransformRequestBody(ep),
            Errors = TransformErrors(ep, compilation),
            // What the pipeline can answer with beyond the declared error union. Without it the shipped
            // document promised only success on operations that reject, refuse and conflict.
            // ⚠️ The else branch was Empty, so a query published the success code alone.
            ProblemStatusCodes = ep.IsMutation
                ? new EquatableArray<int>(ep.MutationProblemStatusCodes.ToImmutableArray())
                : new EquatableArray<int>(ep.QueryProblemStatusCodes.ToImmutableArray()),
            Authorization = TransformAuthorization(ep, derivedByOperation),
            DomainAction = ep.IsDomainAction || ep.IsMutation
                ? new ManifestDomainActionRefModel
                {
                    IsDomainAction = ep.IsDomainAction,
                    IsMutation = ep.IsMutation,
                    IsQuery = ep.IsQuery,
                    EntityType = ep.MutationEntityType,
                    ReturnType = ep.DomainActionReturnType
                }
                : null,
            ApiVersions = ep.ApiVersions.Select(v => v.Version).ToImmutableArray(),
            IsDeprecated = ep.ApiVersions.Any(v => v.Deprecated),
            RateLimitPolicy = ep.RateLimit?.Policy,
            CacheDurationSeconds = ep.ResponseCache is { NoStore: false, Duration: > 0 }
                ? ep.ResponseCache.Duration
                : null,
            FileUpload = TransformFileUpload(ep),
            MaxBodySizeBytes = ep.MaxBodySizeBytes is > 0 ? ep.MaxBodySizeBytes : null,
            RequestExamples = ep.RequestExamples,
            ResponseExamples = ep.ResponseExamples,
            IdempotencyHeader = ep.Idempotency is not null &&
                                ep.HttpMethod is not ("Get" or "Head" or "Options")
                ? ep.Idempotency.HeaderName ?? "Idempotency-Key"
                : null,
            McpTool = ep.McpTool
        };
    }

    private static ManifestFileUploadModel? TransformFileUpload(EndpointModel ep)
    {
        if (ep.FormParameters.IsDefaultOrEmpty)
            return null;

        var fileParams = ep.FormParameters.Where(f => f.IsFile).ToImmutableArray();
        if (fileParams.Length == 0)
            return null;

        // Aggregate the constraints across all file fields: the tightest (smallest) size cap and
        // the union of allowed content types best describe the endpoint's upload contract.
        long? maxSize = null;
        var contentTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fileParams)
        {
            if (f.MaxFileSize is { } size && (maxSize is null || size < maxSize))
                maxSize = size;
            foreach (var ct in f.AllowedContentTypes)
                contentTypes.Add(ct);
        }

        return new ManifestFileUploadModel
        {
            MaxFileSizeBytes = maxSize,
            AllowedContentTypes = contentTypes.ToImmutableArray()
        };
    }

    private static string ResolveGroupPrefix(EndpointGroupModel? group)
    {
        if (group is null) return "";
        var parentPrefix = ResolveGroupPrefix(group.Parent);
        var thisPrefix = group.RoutePrefix ?? "";
        return string.IsNullOrEmpty(parentPrefix)
            ? thisPrefix
            : parentPrefix.TrimEnd('/') + "/" + thisPrefix.TrimStart('/');
    }

    private static string? DeriveFromNamespace(string ns)
    {
        // Extract boundary name from namespace: "Showcase.Booking.Reservations.Endpoints" → "Booking"
        var parts = ns.Split('.');
        return parts.Length >= 2 ? parts[1] : null;
    }

    private static ImmutableArray<ManifestParameterModel> TransformParameters(EndpointModel ep)
    {
        var builder = ImmutableArray.CreateBuilder<ManifestParameterModel>();

        foreach (var p in ep.RouteParameters)
            builder.Add(new ManifestParameterModel
            {
                Name = p.Name, In = "path", Type = p.TypeName,
                IsRequired = !p.IsOptional, Constraint = p.Constraint
            });

        foreach (var p in ep.QueryParameters)
            builder.Add(new ManifestParameterModel
            {
                Name = p.Name, In = "query", Type = p.TypeName,
                IsRequired = p.IsRequired || p.IsRequiredByValidation, DefaultValue = p.DefaultValue
            });

        foreach (var p in ep.HeaderParameters)
            builder.Add(new ManifestParameterModel
            {
                Name = p.HeaderName, In = "header", Type = p.TypeName,
                IsRequired = p.IsRequired || p.IsRequiredByValidation, DefaultValue = p.DefaultValue
            });

        foreach (var p in ep.CookieParameters)
            builder.Add(new ManifestParameterModel
            {
                Name = p.CookieName, In = "cookie", Type = p.TypeName,
                IsRequired = p.IsRequired
            });

        AddPagingParameters(ep, builder);

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The two parameters every paged query accepts, published once for both kinds of query.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Paging is a property of <c>QueryIsPaged</c>, and the handler binds it from there — so it
    ///         works at runtime whether or not a contract names it. Without this a scaffolded list would
    ///         publish neither, and the generated client would have no way to ask for page 2; a
    ///         hand-written one would publish <c>Page</c> and <c>PageSize</c> as <b>request-body
    ///         properties on a GET</b>, which the OpenAPI document renders as a GET with a body.
    ///     </para>
    ///     <para>
    ///         Emitted here rather than in either endpoint builder, because the fact belongs to neither:
    ///         it belongs to being paged. Skipped when the endpoint already declares them, so a query
    ///         that names its own <c>Page</c> property does not get a duplicate.
    ///     </para>
    /// </remarks>
    private static void AddPagingParameters(
        EndpointModel ep, ImmutableArray<ManifestParameterModel>.Builder builder)
    {
        if (!ep.QueryIsPaged)
            return;

        AddPagingParameter(builder, "page", ep.PageDefault);
        AddPagingParameter(builder, "pageSize", ep.PageSizeDefault);
    }

    private static void AddPagingParameter(
        ImmutableArray<ManifestParameterModel>.Builder builder, string name, string? defaultValue)
    {
        if (builder.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        builder.Add(new ManifestParameterModel
        {
            Name = name,
            In = "query",
            Type = "int",
            IsRequired = false,
            DefaultValue = defaultValue,
        });
    }

    private static ManifestRequestBodyModel? TransformRequestBody(EndpointModel ep)
    {
        if (ep.BodyProperties.IsDefaultOrEmpty)
            return null;

        // A paged query's Page and PageSize are query parameters, published as such by
        // AddPagingParameters. Landing here too, they would make the document describe a GET carrying
        // a request body — and leave the reader to work out that the body is really a pair of
        // query-string values.
        var bodyProperties = ep.QueryIsPaged
            ? ep.BodyProperties.Where(bp =>
                !string.Equals(bp.Name, "Page", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(bp.Name, "PageSize", StringComparison.OrdinalIgnoreCase))
                .ToImmutableArray()
            : ep.BodyProperties.AsImmutableArray();

        if (bodyProperties.Length == 0)
            return null;

        var props = bodyProperties
            .Select(bp => new ManifestPropertyModel
            {
                Name = bp.Name,
                WireName = bp.JsonName,
                Type = bp.TypeName,
                IsRequired = bp.IsRequired,
                IsNullable = bp.IsNullable,
                IsEnum = bp.IsEnum,
                Constraints = bp.Constraints,
                Summary = bp.Summary,
                Validation = bp.IsRequired
                    ? ImmutableArray.Create(new ManifestValidationRuleModel { Rule = "required" })
                    : ImmutableArray<ManifestValidationRuleModel>.Empty
            })
            .ToImmutableArray();

        // The body is the property itself when the endpoint binds it directly — the document has to
        // say so, or it advertises an envelope the server stopped accepting.
        return new ManifestRequestBodyModel { Properties = props, IsDirect = ep.HasDirectBodyParam };
    }

    private static ImmutableArray<ManifestErrorRefModel> TransformErrors(EndpointModel ep, Compilation? compilation)
    {
        if (ep.ErrorTypes.IsDefaultOrEmpty)
            return ImmutableArray<ManifestErrorRefModel>.Empty;

        return ep.ErrorTypes
            .Select(e =>
            {
                var extensions = ErrorExtensionExtractor.Extract(e.TypeName, compilation);
                var code = e.SimpleName.EndsWith("Error", StringComparison.Ordinal)
                    ? e.SimpleName.Substring(0, e.SimpleName.Length - 5)
                    : e.SimpleName;

                return new ManifestErrorRefModel
                {
                    Type = e.TypeName,
                    Code = ToUpperSnakeCase(code),
                    StatusCode = e.StatusCode,
                    Title = e.SimpleName,
                    Extensions = extensions
                };
            })
            .ToImmutableArray();
    }

    /// <summary>
    ///     The endpoint's authorization as the manifest describes it, including a permission that was
    ///     derived rather than written.
    /// </summary>
    /// <remarks>
    ///     A derived permission is enforced in the action pipeline, not at the HTTP layer, and that single
    ///     enforcement point stays as it is. But the manifest is what the OpenAPI document is built from,
    ///     so leaving the derived requirement out published a contract that understated what a caller
    ///     needs: the endpoint required a permission and said nothing about it. The permission was already
    ///     in the manifest's global list — discovery and seeding could see it — just never against the
    ///     endpoint that requires it.
    /// </remarks>
    private static ManifestAuthorizationModel? TransformAuthorization(
        EndpointModel ep, Dictionary<string, ImmutableArray<string>> derivedByOperation)
    {
        var auth = ep.Authorization;
        if (!derivedByOperation.TryGetValue(ep.FullTypeName.Replace("global::", ""), out var derived))
            derived = ImmutableArray<string>.Empty;

        if (auth is null)
            return derived.IsEmpty
                ? null
                // No authorization was declared, yet the operation carries a derived requirement. Saying
                // nothing here would be the same silence, one level down.
                : new ManifestAuthorizationModel { RequiredPermissions = new EquatableArray<string>(derived) };

        return new ManifestAuthorizationModel
        {
            AllowAnonymous = auth.AllowAnonymous,
            RequiredPermissions = WithDerived(auth.RequiredPermissions, auth.AnyPermissions, derived),
            AnyPermissions = auth.AnyPermissions,
            RequiredRoles = auth.RequiredRoles,
            PolicyName = auth.PolicyName
        };
    }

    /// <summary>
    ///     Adds every derived permission the endpoint does not already name. Auto-derivation only fires
    ///     for an operation nobody gave a requirement, so an overlap means the same permission arrived by
    ///     both routes — listing it twice would describe a requirement that does not exist.
    /// </summary>
    private static EquatableArray<string> WithDerived(
        EquatableArray<string> required, EquatableArray<string> any, ImmutableArray<string> derived)
    {
        if (derived.IsEmpty)
            return required;

        var builder = ImmutableArray.CreateBuilder<string>();
        builder.AddRange(required.AsImmutableArray());
        foreach (var permission in derived)
            if (!builder.Contains(permission) && !any.Contains(permission))
                builder.Add(permission);

        return builder.Count == required.AsImmutableArray().Length
            ? required
            : new EquatableArray<string>(builder.ToImmutable());
    }

    private static ImmutableArray<ManifestTypeModel> BuildTypes(
        ImmutableArray<EntityMetadataModel> entities,
        ImmutableArray<EndpointModel> endpoints,
        Compilation? compilation)
    {
        var types = ImmutableArray.CreateBuilder<ManifestTypeModel>();
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        // 1. Entity types from PersistenceFeature models (when available)
        foreach (var entity in entities.Where(e => e.IsValid && !e.IsAbstract))
        {
            seenTypes.Add(entity.FullTypeName);
            types.Add(new ManifestTypeModel
            {
                Type = entity.FullTypeName,
                SimpleName = entity.TypeName,
                Kind = ManifestTypeKind.Entity,
                Boundary = entity.BoundaryName,
                IdType = entity.IdType,
                Traits = new ManifestTraitsModel
                {
                    IsAuditable = entity.IsAuditable,
                    IsSoftDelete = entity.IsSoftDelete,
                    IsOwnedEntity = entity.IsOwnedEntity,
                    IsScopedEntity = entity.IsScopedEntity,
                    IsTenantEntity = entity.IsTenantEntity,
                    IsConcurrencyAware = entity.IsConcurrencyAware
                },
                Properties = entity.Properties
                    .Where(p => !p.IsNavigation)
                    // Same rule as for a DTO: an entity published in the manifest is described by what
                    // a response can carry, and the serializer strips these.
                    .Where(p => !global::Pragmatic.Contracts.ReservedWireNames.IsReserved(p.Name))
                    .Select(p => new ManifestPropertyModel
                    {
                        Name = p.Name,
                        WireName = p.WireName,
                        Type = p.TypeName,
                        IsRequired = p.IsRequiredForCreate,
                        IsNullable = p.IsNullable,
                        IsEnum = p.IsEnum,
                        // An entity's metadata carries the column length and nothing else about the
                        // values; the rest of its rules live on the operations that write it.
                        Constraints = new WireConstraints { MaxLength = p.MaxLength },
                        Precision = p.Precision,
                        Scale = p.Scale
                    })
                    .ToImmutableArray()
            });
        }

        // 2. Discover entity/DTO types from endpoint response types via Compilation
        if (compilation is not null)
        {
            var responseTypes = CollectResponseTypes(endpoints);
            foreach (var fqn in responseTypes)
                DiscoverTypeFromCompilation(fqn, compilation, seenTypes, types);
        }

        // 3. Error types from endpoints
        var seenErrors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints)
        {
            foreach (var error in endpoint.ErrorTypes)
            {
                if (!seenErrors.Add(error.TypeName)) continue;
                var extensions = ErrorExtensionExtractor.Extract(error.TypeName, compilation);
                var code = error.SimpleName.EndsWith("Error", StringComparison.Ordinal)
                    ? error.SimpleName.Substring(0, error.SimpleName.Length - 5)
                    : error.SimpleName;

                types.Add(new ManifestTypeModel
                {
                    Type = error.TypeName,
                    SimpleName = error.SimpleName,
                    Kind = ManifestTypeKind.Error,
                    ErrorCode = ToUpperSnakeCase(code),
                    ErrorStatusCode = error.StatusCode,
                    ErrorExtensions = extensions
                });
            }
        }

        return types.ToImmutable();
    }

    /// <summary>
    ///     Collects all unique type FQNs an endpoint puts on the wire — what it answers with AND what it
    ///     accepts. Without the request types, a body property or query parameter carrying a DTO or a
    ///     framework enum would be named in the manifest but never described, and the generated client
    ///     could only expose it as 'object' (PRAG2301).
    /// </summary>
    private static HashSet<string> CollectResponseTypes(ImmutableArray<EndpointModel> endpoints)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ep in endpoints)
        {
            // Same fallback as BuildResponseType: naming a response type in the manifest without describing
            // it here would trade "no type" for "an unknown type" — a client still cannot use it.
            var responseType = ep.ResponseType ?? ep.QueryResultType ?? ep.DomainActionReturnType;
            if (!string.IsNullOrEmpty(responseType)) types.Add(responseType!);
            if (ep.MutationEntityType is not null) types.Add(ep.MutationEntityType);
            if (ep.QueryEntityType is not null) types.Add(ep.QueryEntityType);

            // Inbound surface: body, route, query, header and form parameters.
            foreach (var p in ep.BodyProperties) Add(p.TypeName);
            foreach (var p in ep.RouteParameters) Add(p.TypeName);
            foreach (var p in ep.QueryParameters) Add(p.TypeName);
            foreach (var p in ep.HeaderParameters) Add(p.TypeName);
            foreach (var p in ep.FormParameters) Add(p.TypeName);
        }

        return types;

        void Add(string? typeName)
        {
            if (!string.IsNullOrEmpty(typeName)) types.Add(typeName!);
        }
    }

    /// <summary>
    ///     Resolves a type FQN via Compilation and extracts properties for the manifest.
    ///     Handles generics (PagedResult&lt;T&gt;), arrays (T[]), and discovers referenced enums.
    /// </summary>
    private static void DiscoverTypeFromCompilation(
        string fqn, Compilation compilation,
        HashSet<string> seenTypes, ImmutableArray<ManifestTypeModel>.Builder types)
    {
        var cleanFqn = fqn.Replace("global::", "").TrimEnd('?');

        // Use cleanFqn (global::-stripped) as the deduplication key to avoid misses when
        // the same type is referenced once with and once without the global:: prefix.
        if (!seenTypes.Add(cleanFqn)) return;

        // Wrappers are unwrapped BEFORE the well-known filter, never after: a [Query] endpoint answers
        // with PagedResult<TResult> or IReadOnlyList<TResult>, and both wrappers match a well-known
        // prefix (Pragmatic.Persistence.Query / System.). Filtering first discarded TResult together
        // with its wrapper, so no query endpoint had a response schema. The wrapper itself is still
        // never added to the manifest — only the item type it carries is part of the API surface.

        // Array type: T[] → discover element type
        if (cleanFqn.EndsWith("[]"))
        {
            var elementFqn = "global::" + cleanFqn.Substring(0, cleanFqn.Length - 2);
            DiscoverTypeFromCompilation(elementFqn, compilation, seenTypes, types);
            return;
        }

        // Generic type: PagedResult<GuestDto> → discover inner type argument
        if (cleanFqn.Contains('<'))
        {
            var innerType = ExtractGenericArgument(cleanFqn);
            if (innerType is not null)
                DiscoverTypeFromCompilation("global::" + innerType, compilation, seenTypes, types);
            return;
        }

        var symbol = compilation.GetTypeByMetadataName(cleanFqn);

        // A framework ENUM belongs to the API surface even when its namespace is otherwise filtered.
        // IsWellKnownType exists to keep wrappers out (PagedResult, GridFilterRequest), but it also
        // swallowed Pragmatic.Persistence.Query.SortDirection — an enum carried by 9 query parameters,
        // which the client could then only expose as 'object'. Enums are values, not wrappers.
        var isFrameworkEnum = symbol is { TypeKind: TypeKind.Enum }
                              && !cleanFqn.StartsWith("System.", StringComparison.Ordinal);

        // Skip primitives and well-known framework types
        if (!isFrameworkEnum && IsWellKnownType(cleanFqn)) return;
        if (symbol is null) return;

        // Enum type
        if (symbol.TypeKind == TypeKind.Enum)
        {
            types.Add(new ManifestTypeModel
            {
                Type = fqn,
                SimpleName = symbol.Name,
                Kind = ManifestTypeKind.Enum,
                EnumValues = symbol.GetMembers().OfType<IFieldSymbol>()
                    .Where(f => f.HasConstantValue)
                    .Select(f => f.Name)
                    .ToImmutableArray()
            });
            return;
        }

        // Only classes and structs (records included)
        if (symbol.TypeKind != TypeKind.Class && symbol.TypeKind != TypeKind.Struct) return;

        // Computed before the loop because it decides what a collection property means. On an entity
        // a collection is a navigation and descending into it walks the whole object graph; on a DTO
        // it is data the response carries. The rule was written for the first case and applied to
        // both, so KnowledgeItemDetailDto — a type whose reason to exist is the mentions it carries —
        // was published without them, and a client generated from the contract could not read the
        // field the endpoint was written for.
        var isEntity = symbol.AllInterfaces.Any(i => i.Name == "IEntity");

        var properties = ImmutableArray.CreateBuilder<ManifestPropertyModel>();
        var strippedMembers = ImmutableArray.CreateBuilder<string>();
        foreach (var member in GetAllPublicProperties(symbol))
        {
            if (member.IsStatic || member.IsIndexer) continue;
            if (member.GetMethod is null) continue;

            // What the serializer strips is not part of the contract: publishing it announces a field no
            // response carries and a generated client reads a silent default. One list, read from both
            // ends — and read the same way, so the change-tracking half applies to the types that track
            // their changes and the infrastructure half to everything.
            if (global::Pragmatic.Contracts.ReservedWireNames.IsAlwaysStripped(member.Name))
            {
                strippedMembers.Add(member.Name);
                continue;
            }

            if (isEntity && global::Pragmatic.Contracts.ReservedWireNames.IsChangeTracking(member.Name))
                continue;

            // Navigations stay out; a DTO's collections are part of its shape — unless their elements
            // carry no type to describe. A grid adapter's payload really is a list of object: the
            // values are whatever the component sent. Publishing it hands the client an object[] and a
            // diagnostic asking the author to describe a type that by construction has nothing to
            // describe, which is noise standing in for a decision nobody has to make.
            if (IsCollectionType(member.Type) && (isEntity || HasUntypedElement(member.Type)))
                continue;

            var isNullable = member.NullableAnnotation == NullableAnnotation.Annotated
                             || (member.Type.IsValueType && member.Type is INamedTypeSymbol { IsGenericType: true, Name: "Nullable" });
            var propType = member.Type;
            if (isNullable && propType is INamedTypeSymbol { TypeArguments.Length: 1, Name: "Nullable" } nullable)
                propType = nullable.TypeArguments[0];

            var isEnum = propType.TypeKind == TypeKind.Enum;

            properties.Add(new ManifestPropertyModel
            {
                Name = member.Name,
                WireName = WireNameReader.Read(member),
                Type = propType.ToDisplayString(),
                IsNullable = isNullable,
                IsEnum = isEnum,
                IsRequired = !isNullable && !member.Type.IsValueType,
                Constraints = WireConstraintsReader.Read(member)
            });

            // Descend into the property's own type, not just enums. The type table has to be closed
            // transitively: describing FileResponse while leaving its Content/PartialContent types
            // undescribed, or Reservation while leaving LocalDate undescribed, hands the client a type
            // it can only expose as 'object' (PRAG2301). Recursion is bounded by seenTypes, and
            // IsWellKnownType still keeps primitives and framework wrappers out.
            DiscoverTypeFromCompilation("global::" + propType.ToDisplayString(), compilation, seenTypes, types);
        }

        if (properties.Count == 0) return;

        types.Add(new ManifestTypeModel
        {
            Type = fqn,
            SimpleName = symbol.Name,
            Kind = isEntity ? ManifestTypeKind.Entity : ManifestTypeKind.Dto,
            Properties = properties.ToImmutable(),
            StrippedMembers = strippedMembers.ToImmutable()
        });
    }

    private static IEnumerable<IPropertySymbol> GetAllPublicProperties(INamedTypeSymbol symbol)
    {
        var visited = new HashSet<string>();
        var current = symbol;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol prop
                    && prop.DeclaredAccessibility == Accessibility.Public
                    && visited.Add(prop.Name))
                    yield return prop;
            }
            current = current.BaseType;
        }
    }

    /// <summary>Whether a collection's elements are <c>object</c>, and so have no shape to publish.</summary>
    private static bool HasUntypedElement(ITypeSymbol type)
    {
        var element = type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named => named.TypeArguments[0],
            _ => null
        };

        return element?.SpecialType == SpecialType.System_Object;
    }

    private static bool IsCollectionType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol) return true;
        if (type is not INamedTypeSymbol named || !named.IsGenericType) return false;
        var name = named.Name;
        return name is "List" or "IList" or "ICollection" or "IEnumerable"
            or "HashSet" or "IReadOnlyList" or "IReadOnlyCollection"
            or "ImmutableArray" or "ImmutableList" or "ISet" or "IReadOnlySet";
    }

    private static bool IsWellKnownType(string fqn)
    {
        // Narrowed from "Pragmatic.Persistence.Query" to ".Query.Results": the wrappers this filter exists
        // for (PagedResult<T> and friends) all live under .Results, while the whole-namespace form also
        // swallowed input types the caller genuinely sends — DevExpressLoadOptions (.Adapters) arrived in a
        // request body and could only be typed as 'object'. Unwrapping of generics happens before this
        // check, so the wrappers are already resolved through their element type.
        if (fqn.StartsWith("System.", StringComparison.Ordinal)
            || fqn.StartsWith("Pragmatic.Persistence.Query.Results", StringComparison.Ordinal)
            || fqn.StartsWith("Pragmatic.Result", StringComparison.Ordinal))
            return true;

        return fqn is "string" or "int" or "long" or "bool" or "decimal"
            or "double" or "float" or "byte" or "object" or "void";
    }

    private static string? ExtractGenericArgument(string fqn)
    {
        var openIdx = fqn.IndexOf('<');
        var closeIdx = fqn.LastIndexOf('>');
        if (openIdx < 0 || closeIdx <= openIdx) return null;
        return fqn.Substring(openIdx + 1, closeIdx - openIdx - 1);
    }

    private static ImmutableArray<ManifestBoundaryModel> BuildBoundaries(ImmutableArray<EntityMetadataModel> entities)
    {
        return entities
            .Where(e => e.IsValid && !string.IsNullOrEmpty(e.BoundaryName))
            .GroupBy(e => e.BoundaryName!)
            .Select(g => new ManifestBoundaryModel
            {
                Name = g.Key,
                Type = g.First().BoundaryTypeFullName ?? g.Key
            })
            .ToImmutableArray();
    }

    /// <summary>
    ///     The permissions this assembly enforces, with where each one came from. A derived name is on no
    ///     attribute, so it cannot be read off an endpoint symbol — Actions contributes it, keyed by the
    ///     operation type, and only for operations that actually have an endpoint.
    /// </summary>
    private static ImmutableArray<ManifestPermissionModel> ExtractPermissions(
        ImmutableArray<EndpointModel> endpoints,
        ImmutableArray<Core.DerivedPermissionEntry> derivedPermissions)
    {
        // Ordered by name, because the array is part of a published document and its order is a
        // property of that document rather than of the order the pipeline happened to hand the
        // operations over. The neighbouring AsyncApiTemplate orders its channels for the same reason.
        //
        // ⚠️ This overturns a deliberate decision recorded here — "order is preserved exactly as it
        // was ... reordering would rewrite the manifest of every assembly". The rewrite is real and
        // happens once. What it replaces is a manifest that rewrites itself whenever a file moves
        // between folders: measured on a pure relocation of five files, where every route, verb,
        // operationId and per-endpoint permission stayed identical and two of these lists came back
        // in a different order, for no semantic reason at all.
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<ManifestPermissionModel>();

        foreach (var ep in endpoints)
        {
            if (ep.Authorization is null) continue;
            // A query's derived name is on the endpoint model itself — the route is the only place a
            // query is protected — so it arrives here with the rest and is labelled with how it got there.
            var source = ep.PermissionSource ?? "endpoint";
            foreach (var p in ep.Authorization.RequiredPermissions)
                if (permissions.Add(p))
                    builder.Add(new ManifestPermissionModel { Name = p, Source = source });
            foreach (var p in ep.Authorization.AnyPermissions)
                if (permissions.Add(p))
                    builder.Add(new ManifestPermissionModel { Name = p, Source = source });
        }

        if (!derivedPermissions.IsDefaultOrEmpty)
        {
            var exposed = new HashSet<string>(
                endpoints.Select(e => e.FullTypeName.Replace("global::", "")), StringComparer.Ordinal);

            foreach (var entry in derivedPermissions)
            {
                if (exposed.Contains(entry.OperationTypeFqn) && permissions.Add(entry.Name))
                    builder.Add(new ManifestPermissionModel { Name = entry.Name, Source = entry.Source });
            }
        }

        builder.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return builder.ToImmutable();
    }

    private static ImmutableArray<ManifestActionModel> ExtractActions(ImmutableArray<EndpointModel> endpoints)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return endpoints
            .Where(e => e.IsDomainAction || e.IsMutation)
            .Where(e => seen.Add(e.FullTypeName))
            .Select(e => new ManifestActionModel
            {
                Type = e.FullTypeName,
                SimpleName = e.Name ?? e.TypeName,
                Kind = e.IsMutation ? "mutation" : e.IsVoid ? "voidAction" : "action",
                ReturnType = e.DomainActionReturnType,
                EntityType = e.MutationEntityType,
                IsVoid = e.IsVoid,
                Boundary = e.Group?.Tag ?? DeriveFromNamespace(e.Namespace)
            })
            .ToImmutableArray();
    }

    private static string ToUpperSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && !char.IsUpper(name[i - 1]))
                sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
