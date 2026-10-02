using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;
using Pragmatic.Endpoints.Idempotency;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     The duplicate that is refused is told it can come back.
/// </summary>
/// <remarks>
///     <para>
///         A duplicate arriving while the first request is still running receives <b>409</b>, as
///         <c>[Idempotent]</c>'s remark says, rather than waiting to share the first response. Waiting
///         <em>contains</em> the 409 rather than replacing it, since any wait must answer something
///         when it runs out, and the reservation lives an hour.
///     </para>
///     <para>
///         ⚠️ Which makes the refusal's wording the whole of what is left. A client that reads 409 as
///         fatal sees an error for a request that is succeeding, so the response carries this
///         repository's own problem convention — <c>code</c> and <c>retryAfter</c> in the extensions
///         (<c>DefaultProblemDetailsFactory</c>), not a <c>Retry-After</c> header, which nothing here
///         emits.
///     </para>
/// </remarks>
public class ARefusedDuplicateSaysItCanBeRetriedTests
{
    [Fact]
    public async Task ADuplicateInFlight_IsRefusedWithACodeAConsumerCanRead()
    {
        var problem = await RefusedDuplicateAsync().ConfigureAwait(true);

        problem.ProblemDetails.Status.Should().Be(StatusCodes.Status409Conflict);
        problem.ProblemDetails.Extensions.Should().ContainKey("code",
            "a consumer has to tell this apart from a domain conflict on the same status code");
        problem.ProblemDetails.Extensions["code"].Should().Be("idempotency_key_in_use");
    }

    [Fact]
    public async Task ADuplicateInFlight_IsToldToComeBack()
    {
        var problem = await RefusedDuplicateAsync().ConfigureAwait(true);

        problem.ProblemDetails.Extensions.Should().ContainKey("retryAfter",
            "the request being refused is succeeding elsewhere — a 409 that does not say so reads as "
            + "a failure, which is the one cost this decision carries");
    }

    /// <summary>
    ///     The control: a first request is not refused.
    /// </summary>
    /// <remarks>
    ///     Without it, "the duplicate is refused with a code" is equally satisfied by refusing
    ///     everything, which is a working filter turned into a broken one.
    /// </remarks>
    [Fact]
    public async Task AFirstRequest_IsNotRefused()
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync(_ =>
            ValueTask.FromResult<object?>(Results.Text("created", statusCode: 201))).ConfigureAwait(true);

        result.Should().BeOfType<IdempotentReplayResult>("the first caller runs the endpoint");
    }

    /// <summary>Runs one request that never finishes, then a second with the same key.</summary>
    private static async Task<ProblemHttpResult> RefusedDuplicateAsync()
    {
        var harness = new Harness();
        var firstIsRunning = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();

        var first = harness.InvokeAsync(async _ =>
        {
            firstIsRunning.SetResult();
            await releaseFirst.Task.ConfigureAwait(false);
            return (object?)Results.Text("created", statusCode: 201);
        });

        await firstIsRunning.Task.ConfigureAwait(true);

        // The second arrives while the first genuinely holds the reservation — choreographed, not
        // raced: the first is parked inside the endpoint until this one has been answered.
        var second = await harness.InvokeAsync(_ =>
            ValueTask.FromResult<object?>(Results.Text("created", statusCode: 201))).ConfigureAwait(true);

        releaseFirst.SetResult();
        await first.ConfigureAwait(true);

        return second.Should().BeOfType<ProblemHttpResult>(
            "a duplicate that arrives while the first is still running is refused").Subject;
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        private readonly Dictionary<string, IdempotentResponse> _responses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
        private readonly IServiceProvider _services;

        public Harness()
        {
            var cache = new CacheStackMock();

            cache.TryGetAsync.Returns<IdempotentResponse>(args =>
            {
                var key = (string)args[0]!;
                lock (_responses)
                {
                    return new ValueTask<(bool, IdempotentResponse?)>(
                        _responses.TryGetValue(key, out var hit) ? (true, hit) : (false, null));
                }
            });

            cache.SetAsync.Returns<IdempotentResponse>(args =>
            {
                lock (_responses) _responses[(string)args[0]!] = (IdempotentResponse)args[1]!;
                return ValueTask.CompletedTask;
            });

            cache.IncrementAsync.Returns((string key, long delta, TimeSpan? _, CancellationToken _) =>
            {
                lock (_counters)
                {
                    _counters[key] = _counters.TryGetValue(key, out var current) ? current + delta : delta;
                    return new ValueTask<long>(_counters[key]);
                }
            });

            cache.RemoveAsync.Returns((string key, CancellationToken _) =>
            {
                lock (_counters) _counters.Remove(key);
                return ValueTask.CompletedTask;
            });

            _services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ICacheStack>(cache)
                .BuildServiceProvider();
        }

        public async Task<object?> InvokeAsync(EndpointFilterDelegate endpoint)
        {
            var httpContext = new DefaultHttpContext { RequestServices = _services };
            httpContext.Request.Method = "POST";
            httpContext.Request.Path = "/api/orders";
            httpContext.Request.Headers["Idempotency-Key"] = "ordine-1";

            var filter = new IdempotencyEndpointFilter(null, 0, null);
            return await filter
                .InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), endpoint)
                .ConfigureAwait(false);
        }
    }
}
