using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing an Endpoint class for source generation.
/// </summary>
internal sealed partial record EndpointModel
{
    /// <summary>
    ///     The namespace of the endpoint class.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    ///     The name of the endpoint class.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The fully qualified type name with global:: prefix.
    /// </summary>
    public required string FullTypeName { get; init; }

    /// <summary>
    ///     The accessibility modifier (public, internal).
    /// </summary>
    public required string Accessibility { get; init; }

    /// <summary>
    ///     The HTTP method (Get, Post, Put, Patch, Delete).
    /// </summary>
    public required string HttpMethod { get; init; }

    /// <summary>
    ///     The route pattern (e.g., "/api/orders/{id}").
    /// </summary>
    public required string Route { get; init; }

    /// <summary>
    ///     Optional endpoint name for link generation.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    ///     The response type for successful responses.
    ///     Null for VoidEndpoint.
    /// </summary>
    public string? ResponseType { get; init; }

    /// <summary>
    ///     Whether this is a VoidEndpoint (returns 204 No Content).
    /// </summary>
    public required bool IsVoid { get; init; }

    /// <summary>
    ///     Whether this endpoint inherits from a DomainAction.
    /// </summary>
    public required bool IsDomainAction { get; init; }

    /// <summary>
    ///     The name of the type this endpoint answers with when it does not resolve — as the author
    ///     wrote it, because an error symbol has nothing else.
    /// </summary>
    /// <remarks>
    ///     Nothing is generated for the endpoint: its fully-qualified display is the bare name, and the
    ///     contract, the endpoint file and the manifest would all name a type that cannot exist.
    ///     <c>PRAG9001</c> is reported for it here only when the endpoint is <b>not</b> a
    ///     domain action — an action's own transform reports it already, and one cause deserves one line.
    /// </remarks>
    public string? UnresolvedResponseType { get; init; }

    /// <summary>
    ///     Whether this is a VoidDomainAction (vs DomainAction with return).
    /// </summary>
    public bool IsVoidDomainAction { get; init; }

    /// <summary>
    ///     The return type of the DomainAction (for invoker type parameter).
    /// </summary>
    public string? DomainActionReturnType { get; init; }

    /// <summary>
    ///     Whether this endpoint inherits from a Mutation.
    /// </summary>
    public bool IsMutation { get; init; }

    /// <summary>
    ///     The entity type of the Mutation (for invoker type parameter).
    /// </summary>
    public string? MutationEntityType { get; init; }

    /// <summary>
    ///     The mutation mode name ("Create", "Update", "CreateOrUpdate", "Delete", "Restore") from
    ///     [Mutation(Mode = ...)] or the class-name prefix convention (same rules as MutationTransform).
    ///     Drives the default success status code: only Create-mutations default to 201 — an Update
    ///     exposed as POST must not claim a resource was created (B21).
    /// </summary>
    public string? MutationModeName { get; init; }

    /// <summary>
    ///     A factory that turns the entity a mutation returns into the DTO the endpoint answers with —
    ///     a fully qualified static method taking the entity, e.g. <c>global::…GuestReadDto.FromEntity</c>.
    /// </summary>
    /// <remarks>
    ///     Unset means the endpoint answers with what <c>ResponseType</c> says — for a hand-written mutation
    ///     that declares nothing, its id on a create and no body otherwise, and the entity only
    ///     when <c>ReturnType = Entity</c> is written. <c>[Resource]</c> sets it: a scaffolded API must not put <c>OwnerId</c>,
    ///     <c>CreatedBy</c> and <c>IsDeleted</c> on the wire because the invoker happens to return the
    ///     tracked object.
    /// </remarks>
    public string? MutationResponseFactory { get; init; }

    /// <summary>
    ///     A <c>[ReturnsDto&lt;T&gt;]</c> naming a type that does not map from the mutation's entity, for
    ///     PRAG0531. Null when there is nothing wrong.
    /// </summary>
    /// <remarks>
    ///     Carried on the model rather than reported from the transform: the transform runs per syntax
    ///     node and cannot report, and letting it through produces a missing-method error inside a
    ///     generated file, at a line nobody wrote.
    /// </remarks>
    public string? ReturnsDtoWithoutMapFrom { get; init; }

    /// <summary>
    ///     The path a create's response DTO reads through a navigation, for PRAG0533. Null when the
    ///     mutation is not a create, or its response stays within the aggregate.
    /// </summary>
    /// <remarks>
    ///     A mutation writes one aggregate; a response that flattens <c>Workspace.Name</c> spans two.
    ///     On an update the generated query carries the <c>Include</c> and it works; on a create there
    ///     is no query at all — <c>LoadEntityAsync</c> returns null by definition — so the same
    ///     declaration is a 500 waiting for the first request.
    /// </remarks>
    public string? CreateResponseNavigatesTo { get; init; }

    /// <summary>
    ///     Whether this mutation's entity has something the database can reject as a conflict — a
    ///     <c>[LogicKey]</c>/<c>[GeneratedValue]</c> behind a unique index, or <c>[ConcurrencyAware]</c>.
    /// </summary>
    /// <remarks>
    ///     Drives the 409 in the OpenAPI document. It is a property of the entity, not of the operation:
    ///     the rule is enforced by the database and reaches every write that touches the row. Measured
    ///     before it was declared — a duplicate <c>[LogicKey]</c> answers 409 today, and the document
    ///     said 400, 401, 403, 500 and nothing else.
    /// </remarks>
    public bool MutationCanConflict { get; init; }

    /// <summary>
    ///     Whether the generated invoker performs a <c>[TransitionsTo]</c>, which the state machine can
    ///     refuse with a 409.
    /// </summary>
    /// <remarks>
    ///     A property of the operation, for a mutation and a domain action alike: the refusal is the
    ///     invoker's, and the error list the author declares does not have to mention it.
    /// </remarks>
    public bool PerformsTransition { get; init; }

    /// <summary>
    ///     Whether this endpoint class has a [Query&lt;T,R&gt;] attribute.
    ///     When true, the endpoint generates a handler that executes the query via IQueryExecutor.
    /// </summary>
    public bool IsQuery { get; init; }

    /// <summary>
    ///     The property that takes a canonical <c>GridFilterRequest</c>, when the query declares one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A grid request is not a scalar, so it binds from the body rather than from the query
    ///         string, and the route has to be one that carries a body — which is why declaring it makes
    ///         the endpoint's verb matter. PRAG0725 reports the query that asks for a body on a GET.
    ///     </para>
    ///     <para>
    ///         Named on its own instead of taken from <see cref="BodyProperties" />: for a query that
    ///         collection also holds <c>Page</c> and <c>PageSize</c>, which are query-string parameters
    ///         the template renders itself, so binding the whole of it as a body would put paging on the
    ///         wire twice and in the wrong place.
    ///     </para>
    /// </remarks>
    public string? GridRequestPropertyName { get; init; }

    /// <summary>
    ///     The resource policy this endpoint declares with <c>[RequirePolicy&lt;T&gt;]</c>, or null.
    /// </summary>
    /// <remarks>
    ///     Read by <c>QueryHandlerTemplate</c>, which evaluates the policy in the generated query
    ///     endpoint: the filter that evaluates it elsewhere is an <c>IActionFilter</c>, and a query's
    ///     invoker does not run the action-filter chain.
    /// </remarks>
    public string? DeclaredResourcePolicy { get; init; }

    /// <summary>
    ///     The entity type from [Query&lt;TEntity, TResult&gt;]. Used for DbContext.Set&lt;TEntity&gt;().
    /// </summary>
    public string? QueryEntityType { get; init; }

    /// <summary>
    ///     For a domain action, the entities it reaches through the dependencies it declares.
    /// </summary>
    /// <remarks>
    ///     A domain action names no entity of its own, which is why this is a set rather than the single
    ///     <see cref="QueryEntityType" /> / <see cref="MutationEntityType" />: an action composes, and
    ///     composing three mutations touches three entities. Empty for everything else, and empty for an
    ///     action whose dependencies say nothing — see
    ///     <c>EndpointTransform.ParseDomainActionEntities</c> for what is read and what is deliberately
    ///     not inferred.
    /// </remarks>
    public EquatableArray<string> DomainActionEntityTypes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     For a domain action, the dependency types this compilation could not resolve.
    /// </summary>
    /// <remarks>
    ///     Which, in a build that otherwise succeeds, means generated code — a boundary interface this
    ///     generator writes in the same pass. It is how the register knows the operation composes
    ///     something whose entities no type argument names, so that
    ///     <see cref="DeclaresProcessedData" /> becomes the only thing that can answer for it.
    /// </remarks>
    public EquatableArray<string> UnresolvedDependencyTypes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     What the generator can see the operation reaches, its declarations aside: a domain action's
    ///     dependencies, and — on an action or a mutation — every load (<c>[LoadEntity]</c>,
    ///     <c>[LoadEntities]</c>, <c>[LoadFrom]</c>'s query entity, and the signed-in user's entity for
    ///     <c>[LoadCurrentUser]</c> once the user entities are known).
    /// </summary>
    /// <remarks>
    ///     For a domain action it is part of <see cref="DomainActionEntityTypes" />; for a mutation it is what the
    ///     mutation reaches besides its own entity. A <c>[ProcessesData&lt;T&gt;]</c> naming one of these only
    ///     restates it (PRAG2913).
    /// </remarks>
    public EquatableArray<string> InferredEntityTypes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The entities a domain action's <c>[ProcessesData&lt;T&gt;]</c> name.</summary>
    public EquatableArray<string> DeclaredEntityTypes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Whether the operation declares <c>[LoadCurrentUser]</c>, and so reads the signed-in user's entity.</summary>
    public bool LoadsCurrentUser { get; init; }

    /// <summary>The declarations that restate what the generator infers — one PRAG2913 each.</summary>
    /// <remarks>
    ///     None for an operation that composes through a dependency this compilation cannot resolve: there a
    ///     declaration may be answering for what the composition reaches — PRAG2911 asks for exactly that —
    ///     and an Info telling the author to remove the last one would turn into the warning that asked for
    ///     it.
    /// </remarks>
    public IEnumerable<string> RedundantDeclarations
        => UnresolvedDependencyTypes.IsDefaultOrEmpty
            ? DeclaredEntityTypes.Where(d => InferredEntityTypes.Contains(d))
            : [];

    /// <summary>True when the operation carries either form of <c>[ProcessesData]</c>.</summary>
    /// <remarks>
    ///     The generic one names an entity; the non-generic one says "reviewed, reaches none". Both are
    ///     answers, and PRAG2911 fires only on silence.
    /// </remarks>
    public bool DeclaresProcessedData { get; init; }


    /// <summary>True when the operation carries <c>[RecordAccess]</c>.</summary>
    /// <remarks>
    ///     Kept separate from <see cref="CanRecordAccess" /> so that declaring it without the audit
    ///     package can be reported (PRAG2910) instead of silently doing nothing — an operation whose
    ///     author believes its reads are recorded and whose reads are not is the worst of the three
    ///     possible states.
    /// </remarks>
    public bool DeclaresRecordAccess { get; init; }

    /// <summary>True when the recording can actually be emitted: declared, and the audit trail is visible.</summary>
    public bool CanRecordAccess { get; init; }

    /// <summary>
    ///     The result type from [Query&lt;TEntity, TResult&gt;]. Used for IQueryExecutor response.
    /// </summary>
    public string? QueryResultType { get; init; }

    /// <summary>
    ///     Fully qualified boundary type for the query entity (from [BelongsTo&lt;T&gt;] on entity).
    ///     Used for keyed DbContext DI resolution.
    /// </summary>
    public string? QueryBoundaryType { get; init; }

    /// <summary>
    ///     Whether this query has paging (Page/PageSize properties).
    /// </summary>
    public bool QueryIsPaged { get; init; }

    /// <summary>
    ///     What the query declares as its first page and page size, when it declares them.
    /// </summary>
    /// <remarks>
    ///     Paging has framework defaults so a caller may omit both, and those stay — but a query that
    ///     writes <c>PageSize { get; init; } = 50</c> has said something, and answering 20 makes the
    ///     declaration a decoration. Found in a consumer application, where the number in the source
    ///     and the number on the wire were different.
    /// </remarks>
    public string? PageDefault { get; init; }

    /// <inheritdoc cref="PageDefault" />
    public string? PageSizeDefault { get; init; }

    /// <summary>Whether the query declared <c>Single = true</c>: at most one row, 404 when none.</summary>
    public bool QueryIsSingle { get; init; }

    /// <summary>
    ///     The strategy the query declares with <c>[QueryStrategy]</c>, or <c>null</c> for none.
    /// </summary>
    /// <remarks>
    ///     Decides tracking and whether the EF named query filters are lifted for the queryable the
    ///     handler hands the executor. Null emits the tracked, filtered <c>DbSet</c> the handler has
    ///     always used, so a query that declares nothing reads the way it always did.
    /// </remarks>
    public QueryStrategyKind? QueryStrategy { get; init; }

    /// <summary>
    ///     The error types this endpoint can produce.
    /// </summary>
    public EquatableArray<ErrorTypeModel> ErrorTypes { get; init; } = EquatableArray<ErrorTypeModel>.Empty;

    /// <summary>
    ///     Route parameters extracted from the route pattern.
    /// </summary>
    public EquatableArray<RouteParameterModel> RouteParameters { get; init; } =
        EquatableArray<RouteParameterModel>.Empty;

    /// <summary>
    ///     Route parameters from the route pattern that could not be matched to a public property.
    ///     Each entry contains the raw parameter name and the expected PascalCase property name.
    /// </summary>
    public EquatableArray<UnmatchedRouteParameterModel> UnmatchedRouteParameters { get; init; } =
        EquatableArray<UnmatchedRouteParameterModel>.Empty;

    /// <summary>
    ///     Query parameters from public properties.
    /// </summary>
    public EquatableArray<QueryParameterModel> QueryParameters { get; init; } =
        EquatableArray<QueryParameterModel>.Empty;

    /// <summary>
    ///     Header parameters from [FromHeader] properties.
    /// </summary>
    public EquatableArray<HeaderParameterModel> HeaderParameters { get; init; } =
        EquatableArray<HeaderParameterModel>.Empty;

    /// <summary>
    ///     Claim parameters from [FromClaim] properties.
    /// </summary>
    public EquatableArray<ClaimParameterModel> ClaimParameters { get; init; } =
        EquatableArray<ClaimParameterModel>.Empty;

    /// <summary>
    ///     Cookie parameters from [FromCookie] properties.
    /// </summary>
    public EquatableArray<CookieParameterModel> CookieParameters { get; init; } =
        EquatableArray<CookieParameterModel>.Empty;

    /// <summary>
    ///     Form parameters from [FromForm] properties.
    /// </summary>
    public EquatableArray<FormParameterModel> FormParameters { get; init; } =
        EquatableArray<FormParameterModel>.Empty;

    /// <summary>
    ///     Dependencies (private fields) that need injection.
    /// </summary>
    public EquatableArray<DependencyModel> Dependencies { get; init; } = EquatableArray<DependencyModel>.Empty;

    /// <summary>
    ///     Private/protected fields whose concrete type could not be classified as a service or as
    ///     state (PRAG0527). They are NOT injected; the diagnostic makes that visible instead of silent.
    /// </summary>
    public EquatableArray<AmbiguousDependencyInfo> AmbiguousDependencies { get; init; } =
        EquatableArray<AmbiguousDependencyInfo>.Empty;

    /// <summary>
    ///     Body properties for DTO generation.
    /// </summary>
    public EquatableArray<BodyPropertyModel> BodyProperties { get; init; } = EquatableArray<BodyPropertyModel>.Empty;

    /// <summary>
    ///     The JSON shapes this endpoint puts on the wire — its generated request bodies and its
    ///     response type — for the AOT serializer context.
    /// </summary>
    /// <remarks>
    ///     Computed in the transform, where the action's <c>ITypeSymbol</c> still exists: the body DTO is
    ///     generated by this same generator, so the serialization feature has nothing to walk by the time
    ///     it runs. <c>null</c> when the endpoint contributes neither.
    /// </remarks>
    public Serialization.Models.JsonRootContribution? JsonContribution { get; init; }

    /// <summary>
    ///     The group this endpoint belongs to.
    /// </summary>
    public EndpointGroupModel? Group { get; init; }

    /// <summary>
    ///     Authorization configuration.
    /// </summary>
    public AuthorizationModel? Authorization { get; init; }

    /// <summary>
    ///     <c>[ExplicitPermission]</c> as written on a <c>[Query]</c>, unresolved. Read for a query
    ///     alone: an action or a mutation carries it on its own model.
    /// </summary>
    public Actions.Models.ExplicitPermissionModel? ExplicitPermission { get; init; }

    /// <summary>
    ///     How the permission in <see cref="Authorization" /> got there when the auto-derivation
    ///     posture put it there — <c>"auto-derived"</c> or <c>"explicit"</c> — and <c>null</c> for one
    ///     an author wrote. Set by the derivation stage alone, so with the switch off it is always
    ///     <c>null</c>.
    /// </summary>
    public string? PermissionSource { get; init; }

    /// <summary>
    ///     Rate limiting configuration.
    /// </summary>
    public RateLimitModel? RateLimit { get; init; }

    /// <summary>
    ///     Whether <c>[Endpoint(BindBodyDirectly = true)]</c> asks for the body without an envelope.
    /// </summary>
    /// <remarks>
    ///     Opt-in, because the envelope is the published contract of every endpoint that already
    ///     exists. Honoured only where a single complex <c>[FromBody]</c> property makes it possible.
    /// </remarks>
    public bool BindBodyDirectly { get; init; }

    /// <summary>
    ///     Response caching configuration.
    /// </summary>
    public ResponseCacheModel? ResponseCache { get; init; }

    /// <summary>
    ///     API versions for this endpoint.
    /// </summary>
    public EquatableArray<ApiVersionModel> ApiVersions { get; init; } = EquatableArray<ApiVersionModel>.Empty;

    /// <summary>
    ///     Pre-processors to run before the handler.
    /// </summary>
    public EquatableArray<ProcessorModel> PreProcessors { get; init; } = EquatableArray<ProcessorModel>.Empty;

    /// <summary>
    ///     Post-processors to run after the handler.
    /// </summary>
    public EquatableArray<ProcessorModel> PostProcessors { get; init; } = EquatableArray<ProcessorModel>.Empty;

    /// <summary>
    ///     OpenAPI summary.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    ///     OpenAPI description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    ///     OpenAPI tags.
    /// </summary>
    public EquatableArray<string> Tags { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Custom success status code (default based on method and void).
    /// </summary>
    public int? SuccessStatusCode { get; init; }

    /// <summary>
    ///     [CreatedAt] location template for 201 responses; {Prop} tokens are filled from the
    ///     success value's properties. Null → 201 without Location.
    /// </summary>
    public string? CreatedAtTemplate { get; init; }

    /// <summary>
    ///     [MaxBodySize] request body limit in bytes; null → no per-endpoint limit.
    ///     Values ≤ 0 are invalid (PRAG0517) and suppress emission.
    /// </summary>
    public long? MaxBodySizeBytes { get; init; }

    /// <summary>
    ///     [RequestExample] payloads for OpenAPI documentation.
    /// </summary>
    public EquatableArray<ExampleModel> RequestExamples { get; init; } = EquatableArray<ExampleModel>.Empty;

    /// <summary>
    ///     [ResponseExample] payloads for OpenAPI documentation.
    /// </summary>
    public EquatableArray<ResponseExampleModel> ResponseExamples { get; init; } =
        EquatableArray<ResponseExampleModel>.Empty;

    /// <summary>
    ///     [RequireAntiforgery]: emit antiforgery-token metadata and keep antiforgery enabled
    ///     on form endpoints (which otherwise get DisableAntiforgery()).
    /// </summary>
    public bool RequireAntiforgery { get; init; }

    /// <summary>
    ///     [Idempotent] configuration; null when the endpoint is not idempotent.
    ///     Ignored on safe verbs (PRAG0513).
    /// </summary>
    public IdempotencyModel? Idempotency { get; init; }

    /// <summary>
    ///     Whether the endpoint streams Server-Sent Events (StreamingEndpoint /
    ///     StreamingDomainAction base). Success is always 200 text/event-stream.
    /// </summary>
    public bool IsStreamingResponse { get; init; }

    /// <summary>
    ///     Fully qualified stream item type (TItem) for streaming endpoints.
    /// </summary>
    public string? StreamItemTypeName { get; init; }

    /// <summary>
    ///     [Sse] heartbeat interval in seconds; null/0 disables the keep-alive comment.
    /// </summary>
    public int? SseHeartbeatSeconds { get; init; }

    /// <summary>
    ///     [McpTool] configuration; null when the endpoint is not exposed as an MCP tool.
    /// </summary>
    public McpToolModel? McpTool { get; init; }

    /// <summary>
    ///     Optional hint name prefix for programmatic endpoints (e.g. "_Resource.Guest").
    ///     When set, generated files use this prefix instead of the TypeName.
    /// </summary>
    public string? HintPrefix { get; init; }

    /// <summary>
    ///     Optional hint artifact suffix for programmatic endpoints (e.g. "Create" for "_Resource.Guest.Create").
    /// </summary>
    public string? HintSuffix { get; init; }

    /// <summary>
    ///     Location for diagnostic reporting.
    /// </summary>
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();

    /// <summary>
    ///     Reason why this model is invalid, if any.
    /// </summary>
    public InvalidReason InvalidReason { get; init; } = InvalidReason.None;

    /// <summary>
    ///     The most error types an endpoint may declare (PRAG0503).
    /// </summary>
    /// <remarks>
    ///     Six is what the runtime carries: <c>Result&lt;T, …&gt;</c>, the endpoint bases, the domain
    ///     action bases and the mutation bases each stop at six error arguments, and the generated
    ///     <c>MapError</c> switches over the same six. The bases enforce it for the shapes that ship;
    ///     the transform enforces it for any that do not, so the limit is the generator's and not an
    ///     accident of the arities in the package.
    /// </remarks>
    public const int MaxErrorTypes = 6;

    /// <summary>
    ///     Versioned Execute methods for DomainAction-based endpoints with convention versioning.
    ///     Each version has its own filtered body properties.
    /// </summary>
    public EquatableArray<ActionVersionModel> ActionVersions { get; init; } = EquatableArray<ActionVersionModel>.Empty;

    /// <summary>
    ///     Whether the Asp.Versioning.Http package is available in the compilation.
    ///     Required for generating versioned endpoint registrations.
    /// </summary>
    public bool HasAspVersioning { get; init; }

    /// <summary>
    ///     A permission this route enforces that the author never declared and cannot switch off —
    ///     <c>[Autocomplete]</c> derives one from the boundary and the entity.
    /// </summary>
    /// <remarks>
    ///     It travels to the host because only the host knows whether anyone can ever hold it: under
    ///     <c>[AnonymousHost]</c> the route answers 403 for the life of the application, and that is
    ///     PRAG1692. The module cannot know, so the question is not asked there.
    /// </remarks>
    public string? DerivedPermission { get; init; }

    /// <summary>
    ///     Whether the endpoint type implements ISyncValidator (has validation attributes).
    /// </summary>
    public bool HasValidation { get; init; }

    /// <summary>
    ///     Whether the endpoint type is decorated with [NoValidation] to opt out.
    /// </summary>
    public bool HasNoValidation { get; init; }

    /// <summary>
    ///     Filter override contribution from [WithoutFilter] and [FilterMode] attributes.
    /// </summary>
    public FilterOverrideModel? FilterOverrides { get; init; }

    /// <summary>
    ///     When set, the generated DomainAction handler is a multipart/form-data file-upload
    ///     endpoint: it accepts an <c>IFormFile</c> and maps it onto the action's
    ///     <c>FileContent</c> / <c>FileName</c> / <c>FileSize</c> / <c>ContentType</c> members.
    ///     Used by the [HasAttachments] trait upload endpoint.
    /// </summary>
    public AttachmentUploadModel? AttachmentUpload { get; init; }
}

/// <summary>
///     Reasons why an endpoint model is invalid.
/// </summary>
internal enum InvalidReason
{
    /// <summary>No issue.</summary>
    None,

    /// <summary>Type must be a class.</summary>
    NotClass,

    /// <summary>Type must be partial.</summary>
    NotPartial,

    /// <summary>Type must inherit from Endpoint or VoidEndpoint.</summary>
    NotEndpoint,

    /// <summary>Missing route in EndpointAttribute.</summary>
    MissingRoute,

    /// <summary>Too many error types (max 6).</summary>
    TooManyErrors
}
