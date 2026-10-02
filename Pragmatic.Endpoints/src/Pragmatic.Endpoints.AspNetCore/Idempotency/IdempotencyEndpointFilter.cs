using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Endpoints.Idempotency;

/// <summary>
///     Endpoint filter implementing [Idempotent]: requires the idempotency-key header
///     (400 when missing), executes the handler once per (route, key, body-hash) and
///     replays the captured 2xx response for retries.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The endpoint runs in the caller's own execution context, never inside a cache
///         factory.</b> Running it inside <c>GetOrSetAsync</c> would make the filter unusable on any
///         endpoint that requires a permission: inside a <c>HybridCache</c> factory
///         <c>IHttpContextAccessor.HttpContext</c> is null (dotnet/extensions#5648), so
///         <c>ICurrentUser</c> sees no principal and the Actions pipeline refuses every authenticated
///         request with <b>401 "Authentication required"</b>. An endpoint that carries no permission
///         does not show it.
///     </para>
///     <para>
///         Handing the factory a captured <c>HttpContext</c> would be worse than the 401.
///         Stampede protection means <em>one</em> factory invocation serves every concurrent caller,
///         so the endpoint would run under one caller's identity and its response would be delivered
///         to the others — a cross-tenant leak in place of a 401. An HTTP request may not be
///         de-duplicated across principals, and that is why the cache is only read from and
///         written to, never executed inside.
///     </para>
///     <para>
///         Concurrent duplicates are answered instead of shared: the key is reserved with an atomic
///         increment before the endpoint runs, and a second request that finds the reservation held
///         gets <b>409 Conflict</b> — the same answer Stripe gives for a key still in flight, and a
///         defined one, which a shared response is not.
///     </para>
///     <para>
///         ⚠️ The reservation is atomic within a process. Across instances it is only as atomic as
///         the registered <c>ICacheStack</c>: with the default fallback two instances can both admit
///         a duplicate, which needs a backend with a native atomic increment (Redis <c>INCR</c>) to
///         close. Stated rather than implied — it is the same limit the rate limiter carries.
///     </para>
///     <para>
///         Fails fast when Pragmatic.Caching is not registered — [Idempotent] is an explicit
///         opt-in, and silently dropping the guarantee would be a correctness bug.
///     </para>
/// </remarks>
public sealed class IdempotencyEndpointFilter(
    string? headerName,
    int durationSeconds,
    Func<EndpointFilterInvocationContext, string?>? requestHashProvider) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var options = httpContext.RequestServices.GetService(typeof(PragmaticEndpointsOptions))
            as PragmaticEndpointsOptions;

        var header = headerName ?? options?.Idempotency.HeaderName ?? "Idempotency-Key";
        var duration = durationSeconds > 0
            ? durationSeconds
            : options?.Idempotency.DefaultDurationSeconds ?? 3600;

        if (!httpContext.Request.Headers.TryGetValue(header, out var keyValues) ||
            string.IsNullOrWhiteSpace(keyValues.ToString()))
            return Results.Problem($"Missing required idempotency header: {header}", statusCode: 400);

        var cache = CacheStackProvider.ForCategoryOrNull<CacheCategories.Idempotency>(httpContext.RequestServices)
                    ?? throw new InvalidOperationException(
                        "[Idempotent] requires Pragmatic.Caching: register it with AddPragmaticCaching(). " +
                        "Refusing to run without the replay cache — the idempotency guarantee would be silently lost.");

        var bodyHash = requestHashProvider?.Invoke(context) is { } bodyJson
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bodyJson)))
            : await HashRequestBodyAsync(httpContext).ConfigureAwait(false);

        var (tenant, caller) = ScopeOf(httpContext.RequestServices);

        // ⚠️ The query string is in the key for the same reason the body hash is: `?range=today` and
        // `?range=month` are different operations, and re-executing is the conservative direction.
        // Without it one key covered both and the second replayed the first's report.
        var cacheKey =
            $"idempotency:t={tenant}:u={caller}:" +
            $"{httpContext.Request.Method}:{httpContext.Request.Path}{httpContext.Request.QueryString}:" +
            $"{keyValues.ToString()}:{bodyHash}";

        // 1. A completed response replays, and nothing else happens.
        var (found, cached) = await cache
            .TryGetAsync<IdempotentResponse>(cacheKey, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (found && cached is not null)
            return new IdempotentReplayResult(cached);

        // 2. Reserve the key. A concurrent duplicate is told so rather than handed somebody else's
        //    execution: sharing one run across two principals is how a de-duplicating cache turns a
        //    retry into a data leak.
        var reservationKey = cacheKey + ":in-flight";
        var holders = await cache
            .IncrementAsync(reservationKey, 1, TimeSpan.FromSeconds(duration), httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (holders > 1)
            return Results.Problem(
                "A request with this idempotency key is already in flight. Retry to receive its "
                + "response once it has finished.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Idempotency key in use",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    // ⚠️ A code, because 409 is also what a domain conflict answers and a consumer has
                    // to tell them apart. And retryAfter, because the request being refused is
                    // succeeding elsewhere: a 409 that does not say so reads as a failure, which is the
                    // one cost the owner's decision carries. The repository's own problem
                    // convention — DefaultProblemDetailsFactory writes both — rather than a
                    // Retry-After header, which nothing here emits.
                    //
                    // ⚠️ One second is a floor, not a prediction: nothing here knows how long the
                    // request in flight will take.
                    ["code"] = "idempotency_key_in_use",
                    ["retryAfter"] = 1,
                });

        // 2b. Look again, now that the reservation is held.
        //
        // ⚠️ Without this second read the filter had an interleaving with no guard at all: B reads and
        // misses while A is still running, A finishes — storing its response and releasing the
        // reservation — and B then takes the freed reservation and executes the endpoint a second
        // time. It needs neither a second instance nor a lost race on the counter: the
        // increment is atomic and that sequence is a single-process interleaving.
        var (arrivedMeanwhile, meanwhile) = await cache
            .TryGetAsync<IdempotentResponse>(cacheKey, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (arrivedMeanwhile && meanwhile is not null)
        {
            await cache.RemoveAsync(reservationKey, CancellationToken.None).ConfigureAwait(false);
            return new IdempotentReplayResult(meanwhile);
        }

        try
        {
            // 3. The endpoint runs here — in the caller's own context, with its own identity.
            var response = await CaptureAsync(httpContext, context, next, httpContext.RequestAborted)
                .ConfigureAwait(false);

            // Only successful responses are worth replaying; a failure is left uncached so the client
            // can retry with the same key.
            if (response.StatusCode is >= 200 and < 300)
                await cache.SetAsync(
                        cacheKey,
                        response,
                        new CacheEntryOptions { Duration = TimeSpan.FromSeconds(duration) },
                        httpContext.RequestAborted)
                    .ConfigureAwait(false);

            return new IdempotentReplayResult(response);
        }
        finally
        {
            // Released whatever happened: a reservation that outlives a failed request would refuse
            // the retry it exists to allow.
            await cache.RemoveAsync(reservationKey, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs the endpoint against a buffered response body and captures status, content type,
    ///     selected headers, and payload for replay.
    /// </summary>
    /// <remarks>
    ///     The buffer is installed <em>before</em> the endpoint runs, not after. A generated endpoint
    ///     is mapped as a <c>RequestDelegate</c> — the shape that survives an AOT publish — so it
    ///     writes its own response instead of returning an unexecuted <c>IResult</c>. Capturing
    ///     afterwards meant the response had already started and there was nothing left to buffer.
    ///     A hand-written minimal API still returns its result unexecuted; both are handled, and the
    ///     empty buffer is what tells them apart.
    /// </remarks>
    private static async Task<IdempotentResponse> CaptureAsync(
        HttpContext httpContext,
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var originalBody = httpContext.Response.Body;
        using var buffer = new MemoryStream();
        httpContext.Response.Body = buffer;

        try
        {
            var result = await next(context).ConfigureAwait(false);

            if (buffer.Length == 0)
                switch (result)
                {
                    case IResult httpResult:
                        await httpResult.ExecuteAsync(httpContext).ConfigureAwait(false);
                        break;
                    case null:
                        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
                        break;
                    default:
                        await httpContext.Response.WriteAsJsonAsync(result, ct).ConfigureAwait(false);
                        break;
                }

            Dictionary<string, string>? headers = null;
            if (httpContext.Response.Headers.Location.ToString() is { Length: > 0 } location)
                headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Location"] = location
                };

            return new IdempotentResponse(
                httpContext.Response.StatusCode,
                httpContext.Response.ContentType,
                buffer.ToArray(),
                headers);
        }
        finally
        {
            httpContext.Response.Body = originalBody;
        }
    }

    /// <summary>
    ///     Who the replay belongs to: the current tenant and the current caller, or <c>-</c> for each
    ///     the application gives no way to read.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Without these the stored response would cross both.</b> The cache behind the key
    ///         is a singleton, so a key of method, path, header and body hash alone would let two
    ///         tenants sending the same <c>Idempotency-Key</c> to the same route with the same body
    ///         share one response, and the second would never run its own handler.
    ///     </para>
    ///     <para>
    ///         Both segments are always present, with <c>-</c> standing for absent. A conditional key
    ///         would have two shapes and one of them would be the one nobody tests.
    ///         <c>ITenantContext</c> is <c>[ProvidedByHost]</c>, so its absence from the container is
    ///         not a missing registration: it is an application that has no tenants.
    ///     </para>
    ///     <para>
    ///         ⚠️ Consequences worth knowing rather than meeting: the same user retrying from another
    ///         session still replays, because the id is the same; an anonymous retry of an
    ///         authenticated call re-executes.
    ///     </para>
    /// </remarks>
    private static (string Tenant, string Caller) ScopeOf(IServiceProvider services)
    {
        var tenant = services.GetService<ITenantContext>() is { IsResolved: true, TenantId: { } id }
            ? id
            : "-";

        var caller = services.GetService<ICurrentUser>() is { IsAuthenticated: true, Id: { Length: > 0 } who }
            ? who
            : "-";

        return (tenant, caller);
    }

    /// <summary>
    ///     Hashes the raw request body, so the same key with a different body re-executes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It hashes the bytes, not the <em>bound</em> body DTO from the filter's arguments. A
    ///         generated endpoint is mapped as a <c>RequestDelegate</c> and binds its own parameters,
    ///         so that list is empty: hashing it would make every request hash alike, and two different
    ///         bodies under one key would replay the first response instead of re-executing.
    ///     </para>
    ///     <para>
    ///         Filters run before the endpoint reads the body, so the raw stream is still here; it is
    ///         rewound afterwards. Hashing the bytes means two textually different but semantically
    ///         equal bodies re-execute rather than replay — the conservative direction, and the only
    ///         one that does not depend on a serializer round-trip agreeing with itself.
    ///     </para>
    /// </remarks>
    private static async Task<string> HashRequestBodyAsync(HttpContext httpContext)
    {
        var request = httpContext.Request;

        // A form is read as a form, not as bytes, and buffering an upload to hash it would be a
        // poor trade. The key plus the route identify those requests.
        //
        // ⚠️ `ContentLength == 0` and not `is null or 0`. A null length means the client did not
        // declare one — a chunked request, which is the ordinary shape for anything streamed — and
        // reading that as "no body" would make two different bodies produce one key, so the second
        // would replay the first's response.
        if (request.ContentLength == 0 || request.HasFormContentType)
            return "nobody";

        request.EnableBuffering();
        request.Body.Position = 0;

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(request.Body, httpContext.RequestAborted).ConfigureAwait(false);
        request.Body.Position = 0;

        return Convert.ToHexString(hash);
    }
}
