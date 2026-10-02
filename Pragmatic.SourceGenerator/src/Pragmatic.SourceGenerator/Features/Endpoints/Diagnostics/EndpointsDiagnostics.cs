using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Diagnostics;

/// <summary>
///     Diagnostic descriptors for the Endpoints source generator.
///     Diagnostic IDs: PRAG0500-0599
/// </summary>
internal static class EndpointsDiagnostics
{
    private const string Category = "Pragmatic.Endpoints";

    // PRAG0530 is retired. It reported a [RequirePolicy<T>] declared on a query; the generated query
    // endpoint evaluates the policy itself (a query's invoker does not run the action-filter chain), so
    // the declaration means what it says and forbidding it would forbid a working feature. The id is not
    // reused: an id that meant something else in an older build log is worse than a gap.

    // PRAG0500 (endpoint class must be partial) is the companion analyzer's, which reports it on the
    // declaration (NotPartialDiagnosticDescriptors); the generator skips the type silently.

    /// <summary>
    ///     PRAG0501: Endpoint class must take one of the recognised shapes.
    /// </summary>
    /// <remarks>
    ///     The list is rendered from <see cref="EndpointShapes.All" />, the table the transform matches
    ///     against, so a shape added there is named here without anyone remembering to.
    /// </remarks>
    public static readonly DiagnosticDescriptor MustInheritFromEndpoint = new(
        "PRAG0501",
        "Endpoint class must inherit from Endpoint base class",
        $"Endpoint class '{{0}}' must inherit from {EndpointShapes.Listed}",
        Category,
        DiagnosticSeverity.Error,
        true,
        $"Classes decorated with [Endpoint] must inherit from {EndpointShapes.Listed}.");

    /// <summary>
    ///     PRAG0502: Route is required.
    /// </summary>
    public static readonly DiagnosticDescriptor RouteRequired = new(
        "PRAG0502",
        "Route is required",
        "Endpoint '{0}' must specify a route pattern",
        Category,
        DiagnosticSeverity.Error,
        true,
        "The [Endpoint] attribute must specify a route pattern.");

    /// <summary>
    ///     PRAG0503: Too many error types.
    /// </summary>
    public static readonly DiagnosticDescriptor TooManyErrorTypes = new(
        "PRAG0503",
        "Too many error types",
        $"Endpoint '{{0}}' has {{1}} error types, maximum is {Models.EndpointModel.MaxErrorTypes}",
        Category,
        DiagnosticSeverity.Error,
        true,
        $"Endpoints can have at most {Models.EndpointModel.MaxErrorTypes} error types.");

    /// <summary>
    ///     PRAG0504: Route parameter not found.
    /// </summary>
    public static readonly DiagnosticDescriptor RouteParameterNotFound = new(
        "PRAG0504",
        "Route parameter not found",
        "Route parameter '{0}' in endpoint '{1}' does not match any public property. Expected a property named '{2}' (PascalCase of the route parameter)",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "Route parameters should match public properties on the endpoint class.");

    /// <summary>
    ///     PRAG0505: Duplicate endpoint name.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateEndpointName = new(
        "PRAG0505",
        "Duplicate endpoint name",
        "Endpoint name '{0}' is already used by another endpoint",
        Category,
        DiagnosticSeverity.Error,
        true,
        "Endpoint names must be unique for link generation.");

    /// <summary>
    ///     PRAG0507: the type named as the endpoint group cannot be used as one — it does not exist,
    ///     or it exists without <c>[EndpointGroup]</c>. The message says which.
    /// </summary>
    public static readonly DiagnosticDescriptor GroupNotFound = new(
        "PRAG0507",
        "Endpoint group not found",
        "Endpoint group '{0}' referenced by '{1}' {2}",
        Category,
        DiagnosticSeverity.Error,
        true,
        "The referenced endpoint group class must exist and be decorated with [EndpointGroup].");

    /// <summary>
    ///     PRAG0534: a processor the container cannot construct.
    /// </summary>
    /// <remarks>
    ///     The generated handler resolves every declared processor from the request container, and the
    ///     assembly registration puts each one there — but only a type the container can build. An
    ///     abstract processor, or one whose constructors are all non-public, has no registration that
    ///     could work, so it is left out and reported here instead of failing on the first request.
    /// </remarks>
    public static readonly DiagnosticDescriptor ProcessorNotConstructible = new(
        "PRAG0534",
        "Processor cannot be constructed by the container",
        "Processor '{0}' cannot be constructed by the service container ({1}), so no registration is generated for it and requests to this endpoint will fail",
        Category,
        DiagnosticSeverity.Error,
        true,
        "A [PreProcessor<T>]/[PostProcessor<T>] type must be concrete and expose a public constructor: the generated endpoint resolves it from the request services.");

    /// <summary>
    ///     PRAG0512: Property implicitly binds to request body.
    /// </summary>
    public static readonly DiagnosticDescriptor ImplicitBodyProperty = new(
        "PRAG0512",
        "Property implicitly binds to request body",
        "Property '{0}' in endpoint '{1}' implicitly binds to request body — consider adding [FromBody], [FromQuery], or [FromRoute] for clarity",
        Category,
        DiagnosticSeverity.Info,
        true,
        "Add an explicit binding attribute ([FromBody], [FromQuery], [FromRoute], or [FromHeader]) to make the binding source clear.");

    /// <summary>
    ///     PRAG0513: [Idempotent] on a safe HTTP verb.
    /// </summary>
    public static readonly DiagnosticDescriptor IdempotentOnSafeVerb = new(
        "PRAG0513",
        "[Idempotent] on a safe HTTP verb",
        "Endpoint '{0}' uses [Idempotent] with {1}; safe verbs are idempotent by definition, so the filter is not emitted",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "GET/HEAD/OPTIONS requests are idempotent by definition (RFC 9110). Use [ResponseCache] for caching semantics instead.");

    /// <summary>
    ///     PRAG0514: HEAD endpoint declares a response type.
    /// </summary>
    public static readonly DiagnosticDescriptor HeadEndpointHasResponseBody = new(
        "PRAG0514",
        "HEAD endpoint declares a response type",
        "Endpoint '{0}' uses HttpVerb.Head but declares a response type; HTTP forbids a body on HEAD responses, so the body is suppressed. Prefer VoidEndpoint",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "HEAD responses must not carry a body (RFC 9110). Declare the endpoint as VoidEndpoint, or accept that the generated handler suppresses the response body.");

    /// <summary>
    ///     PRAG0516: [MaxFileSize] has a non-positive byte limit.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidMaxFileSize = new(
        "PRAG0516",
        "Invalid [MaxFileSize] limit",
        "Endpoint '{0}' declares [MaxFileSize({1})]; the limit must be a positive number of bytes",
        Category,
        DiagnosticSeverity.Error,
        true,
        "[MaxFileSize] must specify a positive byte limit. A non-positive value would reject every upload (file.Length > negative is always true).");

    /// <summary>
    ///     PRAG0517: [MaxBodySize] has a non-positive byte limit.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidMaxBodySize = new(
        "PRAG0517",
        "Invalid [MaxBodySize] limit",
        "Endpoint '{0}' declares [MaxBodySize({1})]; the limit must be a positive number of bytes",
        Category,
        DiagnosticSeverity.Error,
        true,
        "[MaxBodySize] must specify a positive byte limit. Non-positive values are ignored by the generator.");

    /// <summary>
    ///     PRAG0518: [RequestExample]/[ResponseExample] JSON is not valid.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidExampleJson = new(
        "PRAG0518",
        "Invalid example JSON",
        "Endpoint '{0}' declares an example with invalid JSON: {1}",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "The JSON string passed to [RequestExample]/[ResponseExample] must be valid JSON; the example is still emitted but OpenAPI tooling may reject it.");

    /// <summary>
    ///     PRAG0515: [Autocomplete] entity has no key property.
    /// </summary>
    public static readonly DiagnosticDescriptor AutocompleteMissingKey = new(
        "PRAG0515",
        "Entity has no key property for autocomplete",
        "Property '{0}' on entity '{1}' has [Autocomplete] but the entity has no Id or [Key] property",
        Category,
        DiagnosticSeverity.Error,
        true,
        "The entity must have a property named 'Id', '{EntityName}Id', or decorated with [Key] to generate autocomplete endpoints.");

    /// <summary>
    ///     PRAG0529: two endpoints answer the same verb and route.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ASP.NET Core does not refuse the second registration at startup — it refuses the
    ///         <i>request</i>, with an <c>AmbiguousMatchException</c> that surfaces as a 500 on a route
    ///         that looks correct in the source and in the OpenAPI document. Nothing in the build says
    ///         anything, so the first sign is a failing call.
    ///     </para>
    ///     <para>
    ///         Assembly-wide, which is where a copied attribute lands. Two modules of one host claiming
    ///         the same route are not caught here — the module compiles alone and cannot know — and that
    ///         belongs to the host.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor DuplicateRoute = new(
        "PRAG0529",
        "Two endpoints answer the same verb and route",
        "'{0}' declares [Endpoint({1}, \"{2}\")], which '{3}' already declares. Both are registered and the route then answers 500 on every call, because routing cannot choose between them. Give one of them a different route.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "Two endpoints registered on the same verb and route make the route unusable at run time.");

    /// <summary>
    ///     PRAG0520: [ResponseCache] on a streaming endpoint.
    /// </summary>
    public static readonly DiagnosticDescriptor ResponseCacheOnStreaming = new(
        "PRAG0520",
        "[ResponseCache] on a streaming endpoint",
        "Endpoint '{0}' is a streaming (SSE) endpoint; a text/event-stream response cannot be cached",
        Category,
        DiagnosticSeverity.Error,
        true,
        "SSE responses are long-lived streams — remove [ResponseCache].");

    /// <summary>
    ///     PRAG0554: a shared <c>[ResponseCache]</c> on an endpoint that requires authentication.
    /// </summary>
    /// <remarks>
    ///     ASP.NET Core's default output cache policy does not cache a request that carries credentials, so
    ///     on such an endpoint the attribute keeps nothing, for anybody — measured in
    ///     <c>WhatASharedResponseCacheKeepsTests</c>. Not an error: the route still answers correctly.
    /// </remarks>
    public static readonly DiagnosticDescriptor SharedResponseCacheOnAuthenticatedEndpoint = new(
        "PRAG0554",
        "A shared [ResponseCache] on an endpoint that requires authentication keeps nothing",
        "Endpoint '{0}' declares a shared [ResponseCache] and requires authentication; the output cache does not cache authenticated requests, so nothing is kept",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "Mark the endpoint [AllowAnonymous] if its answer is the same for everybody, use Location = ResponseCacheLocation.Client for a per-browser cache, or remove the attribute.");

    /// <summary>
    ///     PRAG0521: streaming endpoints must use GET or POST.
    /// </summary>
    public static readonly DiagnosticDescriptor StreamingVerbInvalid = new(
        "PRAG0521",
        "Streaming endpoint verb must be GET or POST",
        "Endpoint '{0}' is a streaming (SSE) endpoint mapped to {1}; use GET (EventSource-compatible) or POST",
        Category,
        DiagnosticSeverity.Error,
        true,
        "Browsers' EventSource only supports GET; POST is allowed for streaming queries with a body.");

    /// <summary>
    ///     PRAG0522: [HttpStatus]/[CreatedAt] on a streaming endpoint.
    /// </summary>
    public static readonly DiagnosticDescriptor StatusOverrideOnStreaming = new(
        "PRAG0522",
        "Status override on a streaming endpoint",
        "Endpoint '{0}' is a streaming (SSE) endpoint; the success status is always 200 — remove [HttpStatus]/[CreatedAt]",
        Category,
        DiagnosticSeverity.Error,
        true,
        "SSE responses always open with 200 text/event-stream.");

    /// <summary>
    ///     PRAG0523: versioned handler methods on a streaming endpoint.
    /// </summary>
    public static readonly DiagnosticDescriptor VersioningOnStreaming = new(
        "PRAG0523",
        "Versioning on a streaming endpoint",
        "Endpoint '{0}' declares versioned handler methods; streaming endpoints generate only the default version",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "HandleAsyncV{n}/ExecuteV{n} are not supported on streaming endpoints yet.");

    /// <summary>
    ///     PRAG0524: [PostProcessor] on a streaming endpoint.
    /// </summary>
    public static readonly DiagnosticDescriptor PostProcessorOnStreaming = new(
        "PRAG0524",
        "[PostProcessor] on a streaming endpoint",
        "Endpoint '{0}' is a streaming (SSE) endpoint; there is no final result for post-processors to observe",
        Category,
        DiagnosticSeverity.Error,
        true,
        "Post-processors require a materialized result; SSE streams have none.");

    // ⚠️ PRAG0519 — "[Autocomplete] requires an authentication stack" — is RETIRED, not to be reused.
    // It asked the module whether the host authenticates, which the module cannot know: it can only
    // answer with what the compilation references, and so pins Pragmatic.Identity.AspNetCore to
    // boundary libraries that need nothing from it.
    //
    // The ordinary case — a host with no authentication at all — is PRAG1695, an error on the host.
    // A host that declares [AnonymousHost] and still has a route gating on a derived permission is
    // PRAG1692, reported where both facts are known. The fact travels there on
    // EndpointModel.DerivedPermission.

    /// <summary>
    ///     PRAG0526: two endpoints collapse to the same ApiRoutes member name.
    /// </summary>
    public static readonly DiagnosticDescriptor ApiRouteNameCollision = new(
        "PRAG0526",
        "ApiRoutes member name collision",
        "Endpoint name '{0}' is used by multiple endpoints in boundary '{1}'; only the first gets an ApiRoutes member — set a distinct Name on [Endpoint]",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "ApiRoutes members are named from the endpoint Name (or type name without suffix); duplicates are skipped.");

    /// <summary>
    ///     PRAG0550: [Autocomplete] requires a string property.
    /// </summary>
    public static readonly DiagnosticDescriptor AutocompleteRequiresStringProperty = new(
        "PRAG0550",
        "[Autocomplete] requires a string property",
        "[Autocomplete] on '{0}.{1}' requires a string property, but found '{2}'",
        Category,
        DiagnosticSeverity.Error,
        true,
        "The [Autocomplete] attribute can only be applied to string properties because it generates a Contains() filter.");

    /// <summary>
    ///     PRAG0551: Versioned methods detected but Asp.Versioning.Http is not referenced.
    /// </summary>
    public static readonly DiagnosticDescriptor VersioningRequiresAspVersioning = new(
        "PRAG0551",
        "Versioned methods require Asp.Versioning.Http",
        "'{0}' has versioned methods (ExecuteV2/HandleAsyncV2, etc.) but 'Asp.Versioning.Http' is not referenced. Add the NuGet package to enable versioned endpoint generation. Without it, only the default version endpoint will be generated.",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "Add the Asp.Versioning.Http NuGet package to enable versioned endpoint generation with ApiVersionSet and MapToApiVersion.");

    /// <summary>
    ///     PRAG0527: an endpoint field whose concrete type carries no signal saying whether it is an
    ///     injected service or plain state. The generator does not guess: the field is left alone (and
    ///     therefore null at the first request) and the ambiguity is reported.
    /// </summary>
    public static readonly DiagnosticDescriptor DependencyTypeAmbiguous = new(
        "PRAG0527",
        "Cannot decide whether the field is an injected dependency",
        "Field '{0}' on endpoint '{1}' has concrete type '{2}': the generator cannot tell an injected service from plain state, so the field is NOT injected. Depend on an interface or abstract type, or mark '{2}' with [Service].",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "Only interfaces, abstract types, DbContext subclasses, framework types and [Service]-annotated classes are recognised as DI dependencies.");

    /// <summary>
    ///     PRAG0528: a <c>[RequirePermission]</c> constant on an endpoint that no producer in this
    ///     compilation will generate. The endpoint would be mapped without that permission — any
    ///     authenticated caller reaches it — so say so rather than let it pass. The Actions counterpart
    ///     is PRAG0418.
    /// </summary>
    public static readonly DiagnosticDescriptor PermissionConstNotResolved = new(
        "PRAG0528",
        "Permission constant could not be resolved",
        "Endpoint '{0}' declares [RequirePermission({1})] but that constant could not be resolved to a permission value — it would NOT be enforced (fail-open). Use a literal permission string, or ensure it is a permission this compilation generates.",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "A source generator cannot bind a constant it generates itself, so such a reference is matched against the catalog of permissions this compilation will emit. A path missing from that catalog yields no permission at all.");

    /// <summary>
    ///     PRAG0531: a <c>[ReturnsDto&lt;T&gt;]</c> naming a type that cannot be built from the entity.
    /// </summary>
    /// <remarks>
    ///     The generated handler calls <c>TDto.FromEntity(entity)</c>, which the Mapping feature writes
    ///     for a type carrying <c>[MapFrom&lt;TEntity&gt;]</c>. Without it there is no such method and the
    ///     error lands inside a generated file the author cannot edit, pointing at a line they did not
    ///     write. Said here instead, at the attribute.
    /// </remarks>
    public static readonly DiagnosticDescriptor ReturnsDtoCannotMapFromEntity = new(
        "PRAG0531",
        "This DTO cannot be built from the entity",
        "'{0}' is declared as the response of mutation '{1}', but it does not map from '{2}' — there is no FromEntity to call. Add [MapFrom<{2}>] to it.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "A mutation endpoint projects its entity to the declared DTO through the generated FromEntity.");

    /// <summary>
    ///     PRAG0535: a <c>[ReturnsDto&lt;T&gt;]</c> beside a <c>ReturnType</c> of <c>Id</c> or
    ///     <c>LogicalKey</c>.
    /// </summary>
    /// <remarks>
    ///     Two statements of what the mutation answers with, and the <c>ReturnType</c> wins: the handler
    ///     builds the key record and never reaches the DTO's <c>FromEntity</c>. The DTO applies only to a
    ///     mutation that returns the entity. Before this the author wrote <c>[ReturnsDto&lt;OrderDto&gt;]</c>
    ///     and got <c>{"id": …}</c> on the wire, with nothing said.
    /// </remarks>
    public static readonly DiagnosticDescriptor ReturnsDtoBesideAKeyReturnType = new(
        "PRAG0535",
        "The ReturnType answers, not the declared DTO",
        "'{0}' is declared as the response of mutation '{1}', but [Mutation(ReturnType = {2})] answers with the {3} — the DTO applies only when the mutation returns the entity. Remove [ReturnsDto<{0}>], or remove the ReturnType to answer with the DTO.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "A mutation answers with its key when it declares ReturnType = Id or LogicalKey, and with the entity or its declared DTO otherwise. Declaring both leaves one of them read by nothing.");

    /// <summary>
    ///     PRAG0536: an optional header, query, claim or cookie value on an <c>init</c> property whose initializer the
    ///     generated endpoint cannot reproduce.
    /// </summary>
    /// <remarks>
    ///     An <c>init</c> property can only be set in the object initializer, and there an absent value has
    ///     to be replaced with what the declaration says — which the generated file can write only when it
    ///     is a constant. Anything else (<c>= Guid.NewGuid()</c>, a call, a field) would be evaluated twice or
    ///     not at all. Assigning the value after construction would fail the build with CS8852 inside a
    ///     generated file; this says where the fix is instead.
    /// </remarks>
    public static readonly DiagnosticDescriptor OptionalInitValueWithoutConstantDefault = new(
        "PRAG0536",
        "An optional init property needs a constant default",
        "'{0}' on '{1}' is an optional request value on an init-only property, and its initializer is not a constant the generated endpoint can repeat when the value is absent. Give it a constant default, or a set accessor.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "An absent optional value leaves the property at its declared initializer. On an init property the generated endpoint has to spell that initializer out, so it has to be a literal, a negative literal, an enum member, null or default.");

    /// <summary>
    ///     PRAG0533: a create answering with a DTO that reads through a navigation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A mutation writes one aggregate; more than one is a <c>[CompositeAction]</c>. A response
    ///         DTO that flattens <c>Workspace.Name</c> is therefore asking for something that belongs to
    ///         a <em>different</em> aggregate — and on a create there is nothing to have loaded it from:
    ///         <c>LoadEntityAsync</c> returns null by definition, so there is no query to add an
    ///         <c>Include</c> to and <c>[EagerLoad]</c> is inert.
    ///     </para>
    ///     <para>
    ///         ⚠️ What arrived instead was a 500 at runtime, from a message that lists three remedies
    ///         and does not say that the first one cannot work here. Measured in a consumer
    ///         application: the same DTO worked on the update — where the generated query does get the
    ///         <c>Include</c> — and took three tests down on the create.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor CreateCannotReturnANavigatedDto = new(
        "PRAG0533",
        "A create cannot answer with a DTO that reads through a navigation",
        "'{0}' answers create mutation '{1}' by reading '{2}', which lives on another aggregate. A "
        + "create has no loaded entity to read it from — [EagerLoad] has no query to attach to. Answer "
        + "with a DTO of this aggregate alone, or read the fuller shape back with a query.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "One mutation writes one aggregate; a response that spans two is a read, not a write.");

    /// <summary>
    ///     PRAG0552: a property a form field cannot carry, on an operation whose request is multipart.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An operation that carries a file is <c>multipart/form-data</c>, so there is no JSON body
    ///         and every value arrives as a form field — a string on the wire. A nested object has no
    ///         shape there, and the alternatives are the same three as for a GET: a scalar, a single
    ///         value holding JSON the operation parses itself, or an operation without the file.
    ///     </para>
    ///     <para>
    ///         ⚠️ The mirror of <c>PRAG0532</c>, and for the same reason. The scalars of a multipart
    ///         operation are bound from the form whether or not the author wrote <c>[FromForm]</c> —
    ///         read from <c>body</c>, they would name a variable the handler never declares, a
    ///         <c>CS0103</c> inside generated code whose remedy is an attribute the message does not
    ///         mention. Binding the scalars covers the reachable case; silently omitting what
    ///         is left would replace a build error with an endpoint that quietly ignores a property.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MultipartCannotCarryComplexProperty = new(
        "PRAG0552",
        "A multipart request cannot carry this property",
        "'{0}.{1}' is a {2}, and '{0}' carries a file — a multipart request has no JSON body, so every "
        + "value travels as a form field and a form field cannot carry a nested object. Make it a "
        + "scalar, take it as JSON in a single value, or move the file to an operation of its own.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "An operation that carries a file binds its values from the form, which carries scalars only.");

    /// <summary>
    ///     PRAG0532: a property a GET cannot carry, on an operation exposed as GET.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A GET has no request body, so an operation exposed on one takes its values from the query
    ///         string — which can carry a string, a number, a date or an id, and cannot carry a nested
    ///         object. The alternatives are a scalar the caller can spell in a URL, a single value
    ///         holding JSON the operation parses itself, or a verb that has a body.
    ///     </para>
    ///     <para>
    ///         ⚠️ Said out loud rather than dropped. Before this, a GET bound from the body like every
    ///         other verb and produced a <c>MapGet</c> demanding a JSON body — unreachable by a browser,
    ///         by <c>HttpClient.GetAsync</c> and by a generated client. Binding the scalars from the
    ///         query string fixes the reachable case; silently omitting what is left would replace an
    ///         unusable endpoint with a working one that quietly ignores a parameter.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor GetCannotCarryComplexProperty = new(
        "PRAG0532",
        "A GET cannot carry this property",
        "'{0}.{1}' is a {2}, and '{0}' is exposed as GET — a query string cannot carry a nested object. Make it a scalar, take it as JSON in a single value, or expose the operation on a verb with a body.",
        Category,
        DiagnosticSeverity.Error,
        true,
        "An operation exposed on GET binds its values from the query string, which carries scalars only.");

    /// <summary>
    ///     PRAG0525: <c>[Endpoint]</c> on a member nothing derives a route from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The attribute became legal on a member so that a <b>specification</b> — a static member
    ///         returning <c>Specification&lt;TEntity&gt;</c> and carrying <c>[Query]</c> — could declare
    ///         where its derived query answers. That is the only member shape anything reads.
    ///     </para>
    ///     <para>
    ///         ⚠️ Widening an attribute's targets without this is how a declaration becomes decoration:
    ///         the author writes a route, the build is green, and nothing is mapped. Said here rather
    ///         than left silent, because silence is indistinguishable from a route that works.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor EndpointOnAMemberNothingDerives = new(
        "PRAG0525",
        "[Endpoint] on a member that derives no query",
        "'{0}' carries [Endpoint] but nothing derives a route from it. On a member the attribute is read " +
        "only for a specification — a static member returning Specification<T> that also carries [Query]",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "Add [Query<TEntity>] beside it to derive the query the route executes, or move the [Endpoint] " +
        "onto the operation class that answers.");

    /// <summary>
    ///     PRAG0537: an error type declares a status with <c>[HttpStatus]</c> and answers with another.
    /// </summary>
    /// <remarks>
    ///     The attribute decides what the contract documents; the type's own <c>StatusCode</c> decides what
    ///     a caller receives. Two statements about one thing, and nothing tells a reader which is true:
    ///     the OpenAPI document promises one status and the response carries the other.
    /// </remarks>
    public static readonly DiagnosticDescriptor DeclaredStatusContradictsTheError = new(
        "PRAG0537",
        "An error documents one status and answers with another",
        "'{0}' declares [HttpStatus({1})] and its StatusCode is {2}: the contract documents {1} and a "
            + "caller receives {2}. Make the two agree — the attribute is what the document reads, "
            + "StatusCode is what the response carries.",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "A documented status that the response contradicts is worse than an undocumented one.");

    /// <summary>
    ///     PRAG0538: a published type declares a member the serializer strips from every response.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Tenancy, ownership, the visibility scopes, the concurrency token and the persistence key are
    ///         removed on the way out whatever type carries them — a projection picks them up by accident,
    ///         and leaking them tells a client who owns a record and what makes it visible. A DTO that
    ///         declares one is not wrong; it just does not get it on the wire, and that silence is what this
    ///         says out loud.
    ///     </para>
    ///     <para>
    ///         Info rather than Warning: declaring the name is a legitimate choice — a shape filled from the
    ///         entity may want it for a write path of its own — and the build treats warnings as errors, so a
    ///         Warning would refuse a deliberate declaration rather than describe it.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MemberIsStrippedFromTheWire = new(
        "PRAG0538",
        "A published member never reaches the wire",
        "'{0}.{1}' is removed from every response by the serializer, so it is not published in the API "
            + "contract either: a client generated from it would read a silent default. Rename the member, "
            + "or drop it from the shape the endpoint answers with.",
        Category,
        DiagnosticSeverity.Info,
        true,
        "The name belongs to tenancy, ownership, the concurrency token or the persistence key, which no response carries.");
}
