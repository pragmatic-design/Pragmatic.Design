using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Transforms;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.SourceGenerator.Features.Serialization.Analysis;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Transforms syntax nodes into EndpointModel for code generation.
/// </summary>
/// <remarks>
///     <para>
///         This is a partial class split across multiple files for maintainability:
///         <list type="bullet">
///             <item>
///                 <description>EndpointTransform.cs - Core Transform method and type analysis</description>
///             </item>
///             <item>
///                 <description>EndpointTransform.Security.cs - Authorization, RateLimit, ResponseCache parsing</description>
///             </item>
///             <item>
///                 <description>EndpointTransform.Metadata.cs - API versions, processors, OpenAPI, groups parsing</description>
///             </item>
///             <item>
///                 <description>EndpointTransform.Parameters.cs - Route, query, header, body, dependencies parsing</description>
///             </item>
///             <item>
///                 <description>EndpointTransform.Helpers.cs - Utility methods</description>
///             </item>
///         </list>
///     </para>
/// </remarks>
internal static partial class EndpointTransform
{
    /// <summary>
    ///     Transforms a class with [Endpoint] attribute into an EndpointModel.
    /// </summary>
    public static EndpointModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetNode is not TypeDeclarationSyntax typeDecl ||
            typeDecl is not (ClassDeclarationSyntax or RecordDeclarationSyntax))
            return null;

        var symbol = context.TargetSymbol as INamedTypeSymbol;
        if (symbol is null)
            return null;

        // Check if partial
        var isPartial = typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword);
        if (!isPartial)
            return new EndpointModel
            {
                Namespace = symbol.ContainingNamespace.ToDisplayString(),
                TypeName = symbol.Name,
                FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                HttpMethod = "Get",
                Route = string.Empty,
                IsVoid = false,
                IsDomainAction = false,
                InvalidReason = InvalidReason.NotPartial,
                LocationInfo = LocationInfo.From(typeDecl.Identifier.GetLocation())
            };

        // Parse [Endpoint] attribute
        var endpointAttr = context.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == AttributeNames.Endpoint);

        if (endpointAttr is null)
            return null;

        var httpMethod = "Get";
        var route = string.Empty;
        string? name = null;

        // Parse attribute arguments
        if (endpointAttr.ConstructorArguments.Length >= 2 && endpointAttr.ConstructorArguments[0].Value is int methodValue)
            // First arg is HttpVerb enum
            httpMethod = HttpVerbNames.FromValue(methodValue);

        if (endpointAttr.ConstructorArguments.Length >= 2)
        {
            route = endpointAttr.ConstructorArguments[1].Value?.ToString() ?? string.Empty;

            // Normalize: ensure leading / for consistency (matches group prefix normalization pattern)
            if (route.Length > 0 && route[0] != '/')
                route = "/" + route;
        }

        var bindBodyDirectly = false;

        // Parse named arguments
        foreach (var namedArg in endpointAttr.NamedArguments)
            switch (namedArg.Key)
            {
                case "Name":
                    name = namedArg.Value.Value?.ToString();
                    break;
                case "BindBodyDirectly":
                    bindBodyDirectly = namedArg.Value.Value is true;
                    break;
            }

        // Check for DomainAction inheritance FIRST (before Endpoint check)
        var (isDomainAction, isVoidDomainAction, domainActionReturnType, domainActionErrorTypes) =
            AnalyzeDomainAction(symbol);

        // Determine if inherits from Endpoint or VoidEndpoint
        var (isEndpoint, isVoid, responseType, errorTypes) = AnalyzeBaseType(symbol);

        // The shape a mutation puts on the wire instead of the entity, from [ReturnsDto<T>].
        string? mutationResponseDto = null;
        string? returnsDtoWithoutMapFrom = null;
        string? createResponseNavigatesTo = null;
        MutationKeyResponse? mutationKeyResponse = null;
        string? returnsDtoBesideKeyResponse = null;

        // Streaming (SSE) bases: StreamingEndpoint<TItem,...> / StreamingDomainAction<TItem>
        var (isStreamingEndpoint, streamItemType, streamingErrorTypes) = AnalyzeStreamingBaseType(symbol);
        var (isStreamingAction, actionStreamItemType) = AnalyzeStreamingDomainAction(symbol);
        if (isStreamingEndpoint)
        {
            isEndpoint = true;
            responseType = streamItemType;
            errorTypes = streamingErrorTypes;
        }

        // Check for Mutation<T> inheritance
        var (isMutation, mutationEntityType, mutationBoundaryType, mutationErrorTypes, mutationCanConflict,
            impliedIdType) = AnalyzeMutation(symbol, context.SemanticModel.Compilation);

        // Check for [Query<TEntity, TResult>] attribute
        var (isQuery, queryEntityType, queryResultType, queryBoundaryType, queryIsPaged, queryIsSingle) =
            AnalyzeQueryAttribute(symbol);

        // Must inherit from Endpoint<T>/VoidEndpoint, DomainAction/VoidDomainAction, Mutation<T>, or have [Query<T,R>]
        if (!isEndpoint && !isDomainAction && !isMutation && !isQuery)
            return new EndpointModel
            {
                Namespace = symbol.ContainingNamespace.ToDisplayString(),
                TypeName = symbol.Name,
                FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
                HttpMethod = httpMethod,
                Route = route,
                IsVoid = isVoid,
                IsDomainAction = false,
                InvalidReason = InvalidReason.NotEndpoint,
                LocationInfo = LocationInfo.From(typeDecl.Identifier.GetLocation())
            };

        // For Query endpoints: response is PagedResult<TResult> or IReadOnlyList<TResult>
        if (isQuery)
        {
            responseType = (queryIsPaged, queryIsSingle) switch
            {
                // Single wins over paging: a query that declares both is asking for one row, and the
                // paging properties are then just filters the author left behind.
                (_, true) => queryResultType!,
                (true, _) => $"global::Pragmatic.Persistence.Query.Results.PagedResult<{queryResultType}>",
                _ => $"global::System.Collections.Generic.IReadOnlyList<{queryResultType}>"
            };
            isVoid = false;
            // Default to GET for queries
            if (httpMethod == "Get" || httpMethod == "Post")
            { } // Keep user's choice
        }
        // For Mutation endpoints: entity type is the response unless a DTO is declared, merge error types
        else if (isMutation)
        {
            // A mutation returns the tracked entity, which is the right answer for an in-process caller
            // and the wrong one over HTTP: serialised, it carries OwnerId, CreatedBy, IsDeleted and every
            // other column the server owns. [ReturnsDto<T>] on the mutation says what goes on the wire
            // instead — the same attribute, on the same kind of type, as on a scaffolded operation.
            //
            // A mutation that declares nothing does not answer with the entity.
            (mutationResponseDto, returnsDtoWithoutMapFrom) = ParseReturnsDto(symbol, mutationEntityType, context.SemanticModel.Compilation);

            // PRAG0533: a create answering through a navigation. Asked only of a create, because the
            // same DTO is correct on an update — there the generated query carries the Include.
            if (mutationResponseDto is not null && DetectMutationModeName(symbol) == "Create")
                createResponseNavigatesTo = NavigatedPathOfDeclaredDto(symbol);
            responseType = mutationResponseDto ?? mutationEntityType;

            // [Mutation(ReturnType = Id | LogicalKey)] answers with a record in place of the entity.
            mutationKeyResponse = ReadMutationKeyResponse(symbol, context.SemanticModel.Compilation);
            if (mutationKeyResponse is not null)
            {
                responseType = mutationKeyResponse.TypeName;

                // PRAG0535: a declared DTO beside the key is read by nothing — the handler answers the
                // key record and never reaches FromEntity. The DTO's own checks stand down with it: a
                // PRAG0531 asking for [MapFrom] would have the author fix a declaration nothing reads.
                if (ReturnsDtoParser.Read(symbol) is { } deadDto)
                {
                    returnsDtoBesideKeyResponse = deadDto.Name;
                    mutationResponseDto = null;
                    returnsDtoWithoutMapFrom = null;
                    createResponseNavigatesTo = null;
                }
            }
            // The entity is never the default answer: it is the persistence
            // shape, partial — the invoker loads only what the mutation writes, so an unloaded collection
            // went on the wire as [] — and every public column. A mutation that declares nothing answers:
            //   - a create, the id of the row it made: 201 with {"id": …}, as ReturnType = Id — when what it
            //     creates is an entity, which is what has one ([Entity] or IEntity; PersistenceId itself is
            //     generated, so this transform cannot see it);
            //   - anything else, nothing: 204. A delete included (the removed row, serialised, can
            //     carry the password hash of an account). A create of a type that is not an entity has no
            //     id to give, and answers nothing too.
            // [ReturnsDto<T>], a key ReturnType, or ReturnType = Entity said in so many words still decide.
            // In process the invoker still returns the entity.
            var mode = DetectMutationModeName(symbol);
            var declaresNothing = mutationResponseDto is null && mutationKeyResponse is null && !DeclaresEntityReturn(symbol);
            if (declaresNothing && mode is "Create" or "CreateOrUpdate"
                && EntityOfMutation(symbol) is { } createdEntity
                && Actions.Transforms.EntityTypeHelpers.GetEntityKeyType(createdEntity, context.SemanticModel.Compilation) is not null)
            {
                mutationKeyResponse = IdResponseOf(symbol, context.SemanticModel.Compilation);
                responseType = mutationKeyResponse.TypeName;
            }

            isVoid = declaresNothing && mutationKeyResponse is null;
            if (!mutationErrorTypes.IsEmpty)
                errorTypes = mutationErrorTypes;
        }
        // For DomainAction endpoints, use domain action return type as response type
        else if (isDomainAction)
        {
            responseType = domainActionReturnType;
            isVoid = isVoidDomainAction;

            // The errors the action declares on its base. Empty when the author used the untyped
            // DomainAction<TReturn>, whose Execute returns Result<TReturn, IError> and says nothing.
            if (!domainActionErrorTypes.IsEmpty)
                errorTypes = domainActionErrorTypes;
        }

        // Parse other attributes
        var authorization = ParseAuthorization(symbol, context.SemanticModel.Compilation);
        var declaredResourcePolicy = ParseResourcePolicy(symbol);
        var rateLimit = ParseRateLimit(symbol);
        var responseCache = ParseResponseCache(symbol);
        var apiVersions = ParseApiVersions(symbol);
        var preProcessors = ParseProcessors(symbol, true);
        var postProcessors = ParseProcessors(symbol, false);
        var (summary, description, tags) = ParseOpenApiMetadata(symbol);
        var successStatusCode = ParseSuccessStatusCode(symbol);
        var createdAtTemplate = ParseCreatedAtTemplate(symbol);
        var maxBodySizeBytes = ParseMaxBodySize(symbol);
        var (requestExamples, responseExamples) = ParseExamples(symbol);
        var requireAntiforgery = HasAttribute(symbol, EndpointAttributeNames.RequireAntiforgery);
        var idempotency = ParseIdempotency(symbol);
        var sseHeartbeatSeconds = ParseSseHeartbeat(symbol);
        var mcpTool = ParseMcpTool(symbol);
        // Membership lives on an attribute of its own, not among [Endpoint]'s parameters: that one
        // says the verb and the route, and nothing else.
        var group = ParseGroup(GroupOf(symbol));
        var (routeParameters, unmatchedRouteParameters) = ParseRouteParameters(route, symbol, impliedIdType);
        var headerParameters = ParseHeaderParameters(symbol);
        var claimParameters = ParseClaimParameters(symbol);
        var cookieParameters = ParseCookieParameters(symbol);
        var formParameters = ParseFormParameters(symbol);
        var queryParameters = ParseQueryParameters(symbol, routeParameters, headerParameters, claimParameters, cookieParameters, isQuery);
        var (dependencies, ambiguousDependencies) = ParseDependencies(symbol, context.SemanticModel.Compilation, ct);
        var bodyProperties = ParseBodyProperties(symbol, routeParameters, queryParameters, headerParameters, claimParameters, cookieParameters, formParameters, context.SemanticModel.Compilation);

        // Detect validation support
        var hasValidation = ImplementsISyncValidator(symbol);
        var hasNoValidation = HasAttribute(symbol, EndpointAttributeNames.NoValidation);

        // Detect versioned methods: ExecuteV{n} for DomainAction, HandleAsyncV{n} for Endpoint
        var actionVersions = isDomainAction
            ? ParseActionVersions(symbol, bodyProperties)
            : ParseEndpointVersions(symbol, bodyProperties);

        // Check if Asp.Versioning.Http is available (required for versioned endpoint generation)
        var hasAspVersioning = actionVersions.Length > 1 &&
                               context.SemanticModel.Compilation
                                   .GetTypeByMetadataName("Asp.Versioning.ApiVersion") is not null;

        // Check if ASP.NET Core rate limiting is available (for inline [RateLimit] policies)
        var hasAspNetRateLimiting = context.SemanticModel.Compilation
                                        .GetTypeByMetadataName("Microsoft.AspNetCore.RateLimiting.RateLimiterOptions") is not null;

        // Clear inline rate limit if the runtime isn't available
        if (rateLimit is { Policy: null or "" } && !hasAspNetRateLimiting)
            rateLimit = rateLimit with { Requests = 0 }; // Suppress inline generation

        var filterOverrides = FilterOverrideParser.Parse(symbol);

        // Reads are recorded only where an operation asks for it. Availability is checked separately so
        // that asking without the audit package is reported rather than silently ignored.
        var declaresRecordAccess = HasAttribute(symbol, "Pragmatic.Privacy.RecordAccessAttribute");
        var hasAuditTrail = context.SemanticModel.Compilation
                                .GetTypeByMetadataName("Pragmatic.Audit.IAuditTrail") is not null;

        var model = new EndpointModel
        {
            Namespace = symbol.ContainingNamespace.ToDisplayString(),
            TypeName = symbol.Name,
            FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            HttpMethod = httpMethod,
            Route = route,
            Name = name,
            BindBodyDirectly = bindBodyDirectly,
            ResponseType = responseType,
            UnresolvedResponseType = UnresolvedResponseType(symbol),
            IsVoid = isVoid,
            IsDomainAction = isDomainAction,
            IsVoidDomainAction = isVoidDomainAction,
            DomainActionReturnType = domainActionReturnType,
            IsMutation = isMutation,
            MutationEntityType = mutationEntityType,
            MutationModeName = isMutation ? DetectMutationModeName(symbol) : null,
            MutationResponseFactory = mutationResponseDto is null ? null : $"{mutationResponseDto}.FromEntity",
            MutationKeyResponseType = mutationKeyResponse?.TypeName,
            MutationReturnsId = mutationKeyResponse?.ReturnsId ?? false,
            MutationKeyResponseProperties = mutationKeyResponse?.Properties ?? ImmutableArray<Actions.Models.MutationKeyPartModel>.Empty,
            ReturnsDtoBesideKeyResponse = returnsDtoBesideKeyResponse,
            CreateResponseNavigatesTo = createResponseNavigatesTo,
            ReturnsDtoWithoutMapFrom = returnsDtoWithoutMapFrom,
            MutationCanConflict = mutationCanConflict,
            PerformsTransition = Actions.Transforms.TransitionReader.InvokerPerformsTransition(symbol),
            IsQuery = isQuery,
            QueryEntityType = queryEntityType,
            DomainActionEntityTypes = isDomainAction
                ? ParseDomainActionEntities(symbol)
                : ImmutableArray<string>.Empty,
            UnresolvedDependencyTypes = isDomainAction
                ? ParseUnresolvedDependencies(symbol)
                : ImmutableArray<string>.Empty,
            DeclaresProcessedData = isDomainAction && ParseDeclaresProcessedData(symbol),
            InferredEntityTypes = isDomainAction || isMutation
                ? ParseInferredEntities(symbol, isDomainAction)
                : ImmutableArray<string>.Empty,
            DeclaredEntityTypes = isDomainAction
                ? ParseDeclaredEntities(symbol)
                : ImmutableArray<string>.Empty,
            LoadsCurrentUser = (isDomainAction || isMutation) && ParseLoadsCurrentUser(symbol),
            DeclaresRecordAccess = declaresRecordAccess,
            CanRecordAccess = declaresRecordAccess && hasAuditTrail,
            QueryResultType = queryResultType,
            // Reused for the derived-permission boundary slug; an endpoint is either a query or a mutation,
            // so coalescing the two boundary sources is unambiguous.
            QueryBoundaryType = queryBoundaryType ?? mutationBoundaryType,
            QueryIsPaged = queryIsPaged,
            PageDefault = queryIsPaged ? PagingDefaultOf(symbol, "Page") : null,
            PageSizeDefault = queryIsPaged ? PagingDefaultOf(symbol, "PageSize") : null,
            QueryIsSingle = queryIsSingle,
            // Asked only of a query: the same attribute on a mutation would be describing the load of
            // a row that is about to be written, where untracked is not an option and the filters are
            // already governed by [FilterMode] / [WithoutFilter<T>].
            QueryStrategy = isQuery ? QueryStrategyParser.Read(symbol) : null,
            ErrorTypes = errorTypes,
            RouteParameters = routeParameters,
            UnmatchedRouteParameters = unmatchedRouteParameters,
            QueryParameters = queryParameters,
            HeaderParameters = headerParameters,
            ClaimParameters = claimParameters,
            CookieParameters = cookieParameters,
            FormParameters = formParameters,
            Dependencies = dependencies,
            AmbiguousDependencies = ambiguousDependencies,
            BodyProperties = bodyProperties,
            GridRequestPropertyName = isQuery ? GridRequestProperty(symbol) : null,
            Group = group,
            Authorization = authorization,
            // A query's [ExplicitPermission] has nowhere else to live: there is no action model for it.
            ExplicitPermission = isQuery ? Actions.Transforms.ActionTransform.ParseExplicitPermission(symbol, context.SemanticModel.Compilation) : null,
            DeclaredResourcePolicy = declaredResourcePolicy,
            RateLimit = rateLimit,
            ResponseCache = responseCache,
            ApiVersions = apiVersions,
            PreProcessors = preProcessors,
            PostProcessors = postProcessors,
            Summary = summary,
            Description = description,
            Tags = tags,
            SuccessStatusCode = successStatusCode,
            CreatedAtTemplate = createdAtTemplate,
            MaxBodySizeBytes = maxBodySizeBytes,
            RequestExamples = requestExamples,
            ResponseExamples = responseExamples,
            RequireAntiforgery = requireAntiforgery,
            Idempotency = idempotency,
            IsStreamingResponse = isStreamingEndpoint || isStreamingAction,
            StreamItemTypeName = streamItemType ?? actionStreamItemType,
            SseHeartbeatSeconds = sseHeartbeatSeconds,
            McpTool = mcpTool,
            ActionVersions = actionVersions,
            HasAspVersioning = hasAspVersioning,
            HasValidation = hasValidation,
            HasNoValidation = hasNoValidation,
            FilterOverrides = filterOverrides,
            InvalidReason = string.IsNullOrEmpty(route) ? InvalidReason.MissingRoute
                : errorTypes.Length > EndpointModel.MaxErrorTypes ? InvalidReason.TooManyErrors
                : InvalidReason.None,
            LocationInfo = LocationInfo.From(typeDecl.Identifier.GetLocation())
        };

        // Asked of the finished model, so which body records exist — and what they are called — is
        // answered in one place for the templates that emit them and the context that must cover them.
        return model with
        {
            JsonContribution = Serialization.Models.JsonRootContribution.Merge(
                Serialization.Models.JsonRootContribution.Merge(
                    Serialization.Models.JsonRootContribution.Merge(
                        JsonBodyDtoShape.For(symbol, model),
                        JsonResponseShape.For(symbol)),
                    JsonComplexFilterShape.For(symbol)),
                mutationKeyResponse?.Json),
        };
    }

    /// <summary>
    ///     The query property that takes a canonical <c>GridFilterRequest</c>, if there is one.
    /// </summary>
    /// <remarks>
    ///     Recognised by type, the way the query's own transform recognises it: the type says what the
    ///     property is, and a marker attribute would be a second way to say the same thing.
    /// </remarks>
    private static string? GridRequestProperty(INamedTypeSymbol symbol)
        => symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p =>
                p is { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public }
                && p.SetMethod is not null
                && p.Type.OriginalDefinition.ToDisplayString()
                    == "Pragmatic.Persistence.Query.Adapters.GridFilterRequest")
            ?.Name;
}
