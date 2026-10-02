using System.Collections.Immutable;
using System.Linq;
using System.Collections.Generic;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

internal sealed partial record EndpointModel
{
    /// <summary>
    ///     Every status a mutation endpoint can answer with other than success: the errors it declares
    ///     on its base type, plus the ones its pipeline produces.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On the model rather than in the template that renders <c>ProducesResponseType</c>, because
    ///         two consumers need it and only one had it. The manifest — and therefore the compile-time
    ///         OpenAPI document served at <c>/openapi/v1.json</c> in every environment — carried only the
    ///         errors declared on the base type, so <c>POST /api/properties</c> published a single 200
    ///         for an endpoint that answers 201 and can answer 400, 401, 403, 409 and 500. The runtime
    ///         document was right and the published one was not, which is the worse way round.
    ///     </para>
    ///     <para>
    ///         Each entry is something the pipeline was measured producing, not something it might: 409
    ///         appears only for an entity with a unique key or a concurrency token, 404 only where there
    ///         is a row to fail to find.
    ///     </para>
    /// </remarks>
    public IReadOnlyCollection<int> MutationProblemStatusCodes
    {
        get
        {
            var codes = new SortedSet<int>();
            foreach (var errorType in ErrorTypes)
                codes.Add(errorType.StatusCode);

            // 400 is the body that could not be read — binding, a malformed JSON, a wrong type — and
            // 500 is the fallback for everything unclassified.
            codes.Add(400);
            codes.Add(500);

            // ⚠️ 422 is the body that WAS read and the rules refused, and it is a different answer to
            // a different failure. Publishing 400 for both would advertise a code a mutation does not
            // return for a refused body and omit the one it does: a generated client would handle the
            // branch that never arrives and treat the real rejection as unexpected.
            codes.Add(422);

            // Authorization applies by default at the root group; only [AllowAnonymous] opts out.
            if (Authorization is not { AllowAnonymous: true })
            {
                codes.Add(401);
                codes.Add(403);
            }

            // A load-mode mutation has a row to find first.
            if (MutationModeName is not null and not "Create")
                codes.Add(404);

            // A unique index or a concurrency token: the database refuses the write.
            if (MutationCanConflict || PerformsTransition)
                codes.Add(409);

            return codes;
        }
    }

    /// <summary>
    ///     The same question for everything that is not a mutation: a query, a domain action, an
    ///     endpoint that stands on its own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ There was no such list. The manifest filled <c>ProblemStatusCodes</c> for mutations
    ///         and left it <b>empty</b> for everything else, so every query and every domain action
    ///         published the success code alone — <c>GET /api/orders/{id}</c> promised only 200 while
    ///         answering 404, measured by <c>ThePublishedResponses</c>. A generated client treats an
    ///         undeclared status as an unexpected response, which is the failure it handles worst.
    ///     </para>
    ///     <para>
    ///         Each entry is tied to something on the endpoint, not added for safety: promising a code
    ///         that never arrives makes a client handle a dead branch, and is the mirror of the same
    ///         defect. That is why <c>401</c> and <c>403</c> stay out of an <c>[AllowAnonymous]</c>
    ///         operation, and why a route with no parameter gets no <c>404</c> — a list without a row
    ///         to miss cannot miss it.
    ///     </para>
    /// </remarks>
    public IReadOnlyCollection<int> QueryProblemStatusCodes
    {
        get
        {
            var codes = new SortedSet<int>();
            foreach (var errorType in ErrorTypes)
                codes.Add(errorType.StatusCode);

            codes.Add(500);

            // Anything the caller sends can fail to bind.
            if (RouteParameters.Count > 0 || BodyProperties.Count > 0 || QueryParameters.Count > 0)
                codes.Add(400);

            // ⚠️ But not everything can be read and then refused. A route parameter that does not
            // parse never reaches validation — routing answers 400 first — so an operation whose only
            // input is an id in the path cannot answer 422, and saying it does makes a generated
            // client handle a branch that never arrives. A body or a query parameter can carry a value
            // the rules reject: `page=-1` answers 422, measured.
            if (BodyProperties.Count > 0 || QueryParameters.Count > 0)
                codes.Add(422);

            if (Authorization is not { AllowAnonymous: true })
            {
                codes.Add(401);
                codes.Add(403);
            }

            // A route parameter is an address, and an address can point at nothing.
            if (RouteParameters.Count > 0)
                codes.Add(404);

            // The state machine refuses a move from the state the row is in.
            if (PerformsTransition)
                codes.Add(409);

            return codes;
        }
    }

    /// <summary>
    ///     Number of body properties that have no explicit binding attribute.
    ///     Used to emit PRAG0512 diagnostic when threshold is exceeded.
    /// </summary>
    public int ImplicitBodyPropertyCount => BodyProperties.Count(bp => bp.IsImplicit);

    /// <summary>
    ///     Resolves the hint name for a given artifact type (e.g. "Endpoint", "RequestBody").
    ///     Uses HintPrefix+HintSuffix if set, otherwise falls back to standard VirtualFolderHints.
    /// </summary>
    public string ResolveHintName(string artifact)
        => HintPrefix is not null && HintSuffix is not null
            ? $"{HintPrefix}.{HintSuffix}.{artifact}.g.cs"
            : Core.VirtualFolderHints.ForType(TypeName, artifact, Namespace);

    /// <summary>
    ///     Whether this model is valid for code generation.
    /// </summary>
    /// <remarks>
    ///     A response type the compiler never resolved counts here and carries no
    ///     <see cref="InvalidReason" /> of its own: it is not this endpoint's declaration being wrong,
    ///     it is a name that binds to nothing, and writing it back out is what turned one missing
    ///     <c>using</c> into a page of <c>CS0246</c> in generated files.
    /// </remarks>
    public bool IsValid => InvalidReason == InvalidReason.None && UnresolvedResponseType is null;

    /// <summary>
    ///     Gets the computed success status code based on endpoint type.
    ///     Mutations derive it from their mode — only Create defaults to 201; an Update or
    ///     Restore exposed as POST must not claim a resource was created (B21). Delete
    ///     mutations are void and fall into 204.
    /// </summary>
    public int ComputedSuccessStatusCode => SuccessStatusCode ??
                                            (IsVoid ? 204 :
                                                IsMutation ? (MutationModeName == "Create" ? 201 : 200) :
                                                HttpMethod == "Post" ? 201 : 200);

    /// <summary>
    ///     Whether this endpoint has dependencies that need injection.
    /// </summary>
    public bool HasDependencies => !Dependencies.IsDefaultOrEmpty;

    /// <summary>
    ///     Whether this endpoint has claim parameters from [FromClaim].
    /// </summary>
    public bool HasClaimParams => !ClaimParameters.IsDefaultOrEmpty;

    /// <summary>
    ///     Whether this endpoint has form parameters from [FromForm].
    /// </summary>
    public bool HasFormParams => !FormParameters.IsDefaultOrEmpty;

    /// <summary>
    ///     Whether this endpoint needs a body DTO generated.
    ///     For regular endpoints: always when body properties exist.
    ///     For DomainActions/Mutations: when multiple body properties exist, or when a single
    ///     body property is scalar (string, int, Guid, etc.) since scalars can't be [FromBody] directly.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Never for a query. A query's body is the canonical grid request or nothing: its other
    ///     properties are filters and paging, which travel in the query string, so an envelope built
    ///     from them would publish a request shape the handler does not read.
    /// </remarks>
    public bool NeedsBodyDto => !CarriesNoRequestBody && !IsQuery && !CarriesMultipartRequest
        && !BodyProperties.IsDefaultOrEmpty &&
        ((!IsDomainAction && !IsMutation && !BindBodyDirectly) || BodyProperties.Length > 1
         || (BodyProperties.Length == 1 && BodyProperties[0].IsScalar));

    /// <summary>
    ///     Whether the request is <c>multipart/form-data</c>: a file or a declared form field makes it
    ///     so, and then there is no JSON body at all.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>Every value of a multipart request is a form field, including the ones nobody marked.</b>
    ///     The handler binds the file as its own parameter and emits no body parameter, so reading the
    ///     other properties from <c>body</c> — the shape of a JSON operation — would name a variable
    ///     nobody declared: <c>CS0103</c>, with a remedy the message does not mention. Route, header, claim and query values come from
    ///     their own sources before this, so what is left is a form field and the generator knows it —
    ///     a decision taken at compile time, not looked up at runtime.
    /// </remarks>
    public bool CarriesMultipartRequest => HasFormParams || IsAttachmentUpload;

    /// <summary>
    ///     The operation's own values when the request is multipart: they arrive as form fields, whether
    ///     or not the author wrote <c>[FromForm]</c> on them.
    /// </summary>
    /// <remarks>
    ///     Scalars only, for the same reason as <see cref="QueryBoundProperties" />: a form field is a
    ///     string on the wire and a nested object has no shape there. <see cref="UnbindableOnMultipart" />
    ///     is the rest, and <c>PRAG0552</c> says so instead of dropping them.
    /// </remarks>
    public ImmutableArray<BodyPropertyModel> FormBoundProperties =>
        CarriesMultipartRequest && !CarriesNoRequestBody && !BodyProperties.IsDefaultOrEmpty
            ? BodyProperties.Where(static p => p.IsScalar).ToImmutableArray()
            : ImmutableArray<BodyPropertyModel>.Empty;

    /// <summary>Properties a multipart request cannot carry as form fields.</summary>
    public ImmutableArray<BodyPropertyModel> UnbindableOnMultipart =>
        CarriesMultipartRequest && !CarriesNoRequestBody && !BodyProperties.IsDefaultOrEmpty
            ? BodyProperties.Where(static p => !p.IsScalar).ToImmutableArray()
            : ImmutableArray<BodyPropertyModel>.Empty;

    /// <summary>
    ///     Whether the verb carries no request body, so the operation's values come from the query
    ///     string instead.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A <c>GET</c> binding from the body like every other verb would produce a <c>MapGet</c>
    ///         whose delegate calls <c>ReadFromJsonAsync</c> with no query-string fallback: unreachable
    ///         by a browser, by <c>HttpClient.GetAsync</c>, and by a generated client, because none of
    ///         them send a body on a GET. A GET action that takes no parameters reads no body, so it
    ///         cannot show the difference.
    ///     </para>
    ///     <para>
    ///         <b>GET only, deliberately.</b> DELETE with a body is discouraged but legal and some APIs
    ///         send one; changing it would break callers that already do. HEAD never reaches here.
    ///     </para>
    /// </remarks>
    public bool CarriesNoRequestBody =>
        string.Equals(HttpMethod, "Get", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     The operation's own values, when the verb carries no body and they come from the query string.
    /// </summary>
    /// <remarks>
    ///     Scalars only: a query string has no way to express a nested object, and a caller who needs one
    ///     has to send it as JSON in a single value or use a verb with a body. <c>PRAG0532</c> says so
    ///     rather than silently dropping the property, which is what returning them all here would do.
    /// </remarks>
    public ImmutableArray<BodyPropertyModel> QueryBoundProperties =>
        CarriesNoRequestBody && !BodyProperties.IsDefaultOrEmpty
            ? BodyProperties.Where(static p => p.IsScalar).ToImmutableArray()
            : ImmutableArray<BodyPropertyModel>.Empty;

    /// <summary>
    ///     Properties a query string cannot carry, on a verb that has no body to put them in.
    /// </summary>
    public ImmutableArray<BodyPropertyModel> UnbindableOnQueryString =>
        CarriesNoRequestBody && !BodyProperties.IsDefaultOrEmpty
            ? BodyProperties.Where(static p => !p.IsScalar).ToImmutableArray()
            : ImmutableArray<BodyPropertyModel>.Empty;

    /// <summary>
    ///     Whether this DomainAction/Mutation has exactly one body property that can be passed directly
    ///     as a [FromBody] parameter without generating a wrapper DTO.
    ///     Only complex types (classes, records, arrays, collections) can be passed directly.
    /// </summary>
    public bool HasDirectBodyParam => !CarriesNoRequestBody && !CarriesMultipartRequest
                                      && ((IsQuery && GridRequestPropertyName is not null)
                                          || ((IsDomainAction || IsMutation || BindBodyDirectly)
                                              && BodyProperties.Length == 1 && !BodyProperties[0].IsScalar));

    /// <summary>
    ///     The generated body DTO name.
    /// </summary>
    public string BodyDtoName => Pragmatic.SourceGen.NamingHelper.AppendSuffix(TypeName, "Body");

    /// <summary>
    ///     Every request-body record this endpoint generates. Empty when it takes no generated body.
    /// </summary>
    /// <remarks>
    ///     A versioned endpoint emits one per version, named <c>{Type}V{n}Body</c> — not <c>{Type}Body</c>,
    ///     which is never emitted for it. Anything that needs to name these records asks here.
    /// </remarks>
    public System.Collections.Immutable.ImmutableArray<BodyDtoVariant> BodyDtoVariants
    {
        get
        {
            if (!NeedsBodyDto)
                return System.Collections.Immutable.ImmutableArray<BodyDtoVariant>.Empty;

            if (this is not { HasActionVersioning: true, HasAspVersioning: true })
                return System.Collections.Immutable.ImmutableArray.Create(
                    new BodyDtoVariant(BodyDtoName, BodyProperties.AsImmutableArray(), ApiVersion: null));

            var builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<BodyDtoVariant>();
            foreach (var version in ActionVersions)
            {
                if (version.BodyProperties.IsDefaultOrEmpty)
                    continue;

                builder.Add(new BodyDtoVariant(
                    $"{TypeName}{version.BodyDtoSuffix}Body",
                    version.BodyProperties.AsImmutableArray(),
                    version.VersionString));
            }

            return builder.ToImmutable();
        }
    }

    /// <summary>
    ///     Whether the success value is a <c>FileResponse</c> and must be written as a file
    ///     (Content-Type + Content-Disposition + conditional-request handling) instead of JSON.
    /// </summary>
    /// <remarks>
    ///     <see cref="DomainActionReturnType"/> counts too: a <c>DomainAction&lt;FileResponse&gt;</c> is
    ///     just as much a file endpoint as an <c>Endpoint&lt;FileResponse&gt;</c>. Looking only at
    ///     <see cref="ResponseType"/> made the DomainAction path fall through to
    ///     <c>Results.Ok(success)</c>, which serializes the record — including its <c>Stream</c> — as JSON.
    /// </remarks>
    public bool IsFileResponse
        => Pragmatic.SourceGenerator.Core.FileResponseType.Is(ResponseType) ||
           Pragmatic.SourceGenerator.Core.FileResponseType.Is(DomainActionReturnType);

    /// <summary>
    ///     Whether this DomainAction endpoint has multiple versioned Execute methods.
    /// </summary>
    public bool HasActionVersioning => ActionVersions.Length > 1;

    /// <summary>
    ///     Whether automatic validation should be injected into the generated handler.
    ///     True when the type implements ISyncValidator and does not have [NoValidation].
    /// </summary>
    public bool ShouldAutoValidate => HasValidation && !HasNoValidation;

    /// <summary>
    ///     Whether this endpoint has filter overrides that require IQueryFilterToggle injection.
    /// </summary>
    public bool HasFilterOverrides => FilterOverrides?.HasOverrides == true;

    /// <summary>
    ///     Whether this endpoint is a multipart file-upload DomainAction (e.g. [HasAttachments] upload).
    /// </summary>
    public bool IsAttachmentUpload => AttachmentUpload is not null;
}
