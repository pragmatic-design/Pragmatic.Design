using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Caching;
using Pragmatic.Endpoints.Idempotency;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     IdempotencyEndpointFilter: 400 on missing header, replay of cached 2xx responses,
///     no caching of failures, fail-fast without ICacheStack.
/// </summary>
public class IdempotencyEndpointFilterTests
{
    [Fact]
    public async Task MissingHeader_Returns400()
    {
        var (filter, httpContext, _) = CreateFilter();
        var context = new DefaultEndpointFilterInvocationContext(httpContext);
        var nextCalls = 0;

        var result = await filter.InvokeAsync(context, _ =>
        {
            nextCalls++;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        nextCalls.Should().Be(0);
        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SameKey_SecondCall_ReplaysWithoutReexecuting()
    {
        var (filter, httpContext, _) = CreateFilter();
        httpContext.Request.Headers["Idempotency-Key"] = "key-1";
        var nextCalls = 0;

        EndpointFilterDelegate next = _ =>
        {
            nextCalls++;
            return ValueTask.FromResult<object?>(Results.Text("created", statusCode: 201));
        };

        var first = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);
        var second = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);

        nextCalls.Should().Be(1, "the second request with the same key must replay, not re-execute");
        first.Should().BeOfType<IdempotentReplayResult>();
        second.Should().BeOfType<IdempotentReplayResult>();

        var replayed = await ExecuteAsync((IdempotentReplayResult)second!, httpContext.RequestServices).ConfigureAwait(true);
        replayed.StatusCode.Should().Be(201);
        replayed.Body.Should().Be("created");
    }

    [Fact]
    public async Task DifferentKey_Reexecutes()
    {
        var (filter, httpContext, _) = CreateFilter();
        var nextCalls = 0;

        EndpointFilterDelegate next = _ =>
        {
            nextCalls++;
            return ValueTask.FromResult<object?>(Results.Text("created", statusCode: 201));
        };

        httpContext.Request.Headers["Idempotency-Key"] = "key-1";
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);
        httpContext.Request.Headers["Idempotency-Key"] = "key-2";
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);

        nextCalls.Should().Be(2);
    }

    [Fact]
    public async Task FailureResponse_IsNotCached()
    {
        var (filter, httpContext, _) = CreateFilter();
        httpContext.Request.Headers["Idempotency-Key"] = "key-err";
        var nextCalls = 0;

        EndpointFilterDelegate next = _ =>
        {
            nextCalls++;
            return ValueTask.FromResult<object?>(Results.StatusCode(500));
        };

        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);

        nextCalls.Should().Be(2, "failures must pass through uncached so retries can succeed");
    }

    [Fact]
    public async Task WithoutCacheStack_FailsFast()
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        httpContext.Request.Headers["Idempotency-Key"] = "key-1";
        var filter = new IdempotencyEndpointFilter(null, 0, null);

        var act = async () => await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(httpContext),
            _ => ValueTask.FromResult<object?>(Results.Ok())).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AddPragmaticCaching*");
    }

    private static (IdempotencyEndpointFilter Filter, DefaultHttpContext HttpContext, CacheStackMock Cache) CreateFilter()
    {
        // In-memory GetOrSetAsync honoring ShouldCache — enough to verify replay semantics.
        var store = new Dictionary<string, IdempotentResponse>();
        var counters = new Dictionary<string, long>(StringComparer.Ordinal);
        var cache = new CacheStackMock();

        // The filter reads, reserves, executes, and writes — it does not hand the endpoint to the
        // cache. Wiring GetOrSetAsync here instead would let a regression back in unnoticed: the
        // factory shape is exactly what made [Idempotent] answer 401 on every endpoint with a
        // permission, because inside a HybridCache factory there is no HttpContext to read an
        // identity from.
        cache.TryGetAsync.Returns<IdempotentResponse>(args =>
        {
            var key = (string)args[0]!;
            return new ValueTask<(bool, IdempotentResponse?)>(
                store.TryGetValue(key, out var hit) ? (true, hit) : (false, null));
        });

        cache.SetAsync.Returns<IdempotentResponse>(args =>
        {
            store[(string)args[0]!] = (IdempotentResponse)args[1]!;
            return ValueTask.CompletedTask;
        });

        cache.IncrementAsync.Returns((string key, long delta, TimeSpan? _, CancellationToken _) =>
        {
            counters[key] = counters.TryGetValue(key, out var current) ? current + delta : delta;
            return new ValueTask<long>(counters[key]);
        });

        cache.RemoveAsync.Returns((string key, CancellationToken _) =>
        {
            counters.Remove(key);
            return ValueTask.CompletedTask;
        });

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ICacheStack>(cache)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/notes";

        return (new IdempotencyEndpointFilter(null, 0, null), httpContext, cache);
    }

    /// <summary>
    ///     The endpoint runs in the caller's own context, not inside the cache.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the shape, not a detail. Inside <c>GetOrSetAsync</c>'s factory a
    ///         <c>HybridCache</c> gives no <c>IHttpContextAccessor</c> (dotnet/extensions#5648) — so
    ///         <c>ICurrentUser</c> would see no principal and every authenticated request to an
    ///         endpoint with a permission would come back 401. Handing the factory a captured context
    ///         would be worse: one factory invocation serves every concurrent caller, so the endpoint
    ///         would run under one caller's identity and answer the others with it.
    ///     </para>
    ///     <para>
    ///         Asserted structurally, because that is what can be asserted from here: the filter must
    ///         never reach for the factory API. A behavioural test would need a real HybridCache, and
    ///         the mock in this harness calls whatever factory it is handed — so a behavioural test
    ///         here would stay green with the endpoint inside the factory.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheEndpoint_IsNotExecutedInsideTheCacheFactory()
    {
        var (filter, httpContext, cache) = CreateFilter();
        httpContext.Request.Headers["Idempotency-Key"] = "key-context";

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(
            Results.Text("created", statusCode: 201));

        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next)
            .ConfigureAwait(true);

        cache.GetOrSetAsync.CallCount.Should().Be(0,
            "an HTTP request may not be de-duplicated across principals, so it never runs in a "
            + "shared cache factory");
        cache.TryGetAsync.CallCount.Should().BeGreaterThan(0, "the cache is read from");
        cache.SetAsync.CallCount.Should().BeGreaterThan(0, "and written to");
    }

    /// <summary>
    ///     A second request while the first is in flight is told so.
    /// </summary>
    /// <remarks>
    ///     A defined answer instead of a shared one. The reservation is released when the first
    ///     request finishes, so this is a 409 only while the work is actually running — the retry
    ///     that arrives afterwards replays, as the test above it shows.
    /// </remarks>
    [Fact]
    public async Task AConcurrentDuplicate_IsAnsweredWith409()
    {
        var (filter, httpContext, _) = CreateFilter();
        httpContext.Request.Headers["Idempotency-Key"] = "key-inflight";

        object? second = null;

        // The inner request runs while the outer one holds the reservation, which is what "concurrent"
        // means here — a nested call reproduces it deterministically, where two tasks would race.
        EndpointFilterDelegate next = async _ =>
        {
            second = await filter
                .InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext),
                    _ => ValueTask.FromResult<object?>(Results.Text("second", statusCode: 201)))
                .ConfigureAwait(false);

            return Results.Text("first", statusCode: 201);
        };

        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next)
            .ConfigureAwait(true);

        var replayed = await ExecuteAsync(second, httpContext.RequestServices).ConfigureAwait(true);
        replayed.StatusCode.Should().Be(409,
            "the duplicate is told the work is running, rather than handed another caller's response");
    }

    /// <remarks>
    ///     And the reservation does not outlive the request: a retry after the first one finished must
    ///     replay, not meet a 409 left behind by a key nobody released.
    /// </remarks>
    [Fact]
    public async Task AfterTheFirstRequestFinishes_TheRetryReplays()
    {
        var (filter, httpContext, _) = CreateFilter();
        httpContext.Request.Headers["Idempotency-Key"] = "key-released";
        var nextCalls = 0;

        EndpointFilterDelegate next = _ =>
        {
            nextCalls++;
            return ValueTask.FromResult<object?>(Results.Text("created", statusCode: 201));
        };

        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next)
            .ConfigureAwait(true);
        var second = await filter
            .InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next)
            .ConfigureAwait(true);

        nextCalls.Should().Be(1);
        var replayed = await ExecuteAsync(second, httpContext.RequestServices).ConfigureAwait(true);
        replayed.StatusCode.Should().Be(201, "the reservation was released, so this is a replay");
    }

    private static async Task<(int StatusCode, string Body)> ExecuteAsync(
        object? result, IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await ((IResult)result!).ExecuteAsync(context).ConfigureAwait(false);

        return (context.Response.StatusCode, System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static async Task<(int StatusCode, string Body)> ExecuteAsync(
        IdempotentReplayResult result, IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await result.ExecuteAsync(context).ConfigureAwait(false);

        return (context.Response.StatusCode, System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }
}
