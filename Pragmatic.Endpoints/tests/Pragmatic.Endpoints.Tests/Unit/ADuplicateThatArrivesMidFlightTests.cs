using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;
using Pragmatic.Endpoints.Idempotency;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     A duplicate that arrives while the first request is still running does not execute
///     a second time once the first has finished.
/// </summary>
/// <remarks>
///     <para>
///         The filter read the stored response <b>once</b>, before taking the reservation, and never
///         again after it. That leaves an interleaving with no guard at all:
///     </para>
///     <list type="number">
///         <item><description>B reads the response cache and misses — A is still running.</description></item>
///         <item><description>A finishes: it stores the response and releases the reservation.</description></item>
///         <item><description>B takes the reservation, now free, and executes the endpoint again.</description></item>
///     </list>
///     <para>
///         ⚠️ It needs neither a second instance nor a lost race on the counter: the increment is
///         atomic and the sequence above is a single-process interleaving. Which is why the case below
///         is <b>choreographed</b> rather than run N times and hoped over — A is made to complete at
///         the exact point B reaches its reservation.
///     </para>
/// </remarks>
public class ADuplicateThatArrivesMidFlightTests
{
    [Fact]
    public async Task ADuplicateArrivingWhileTheFirstRuns_DoesNotExecuteAgain()
    {
        var harness = new Harness();
        var executions = 0;

        EndpointFilterDelegate endpoint = _ =>
        {
            executions++;
            return ValueTask.FromResult<object?>(Results.Text("created", statusCode: 201));
        };

        var filter = new IdempotencyEndpointFilter(null, 0, null);

        // The interleaving, placed exactly: the first time B asks for its reservation, A runs to
        // completion — storing its response and releasing the reservation — and only then does B's
        // increment return.
        harness.BeforeIncrement = async () =>
        {
            await filter.InvokeAsync(harness.Invocation(), endpoint).ConfigureAwait(true);
        };

        var second = await filter.InvokeAsync(harness.Invocation(), endpoint).ConfigureAwait(true);

        executions.Should().Be(1,
            "B found no response only because A had not finished yet; by the time B holds the "
            + "reservation A's response is there, and running the endpoint again is the duplicate "
            + "the whole filter exists to prevent");

        second.Should().BeOfType<IdempotentReplayResult>("and what B gets is A's response");
    }

    /// <summary>
    ///     The control: a request that is genuinely first still runs.
    /// </summary>
    /// <remarks>
    ///     Without it, "a duplicate does not execute" is equally satisfied by a filter that never
    ///     executes anything — the re-check under the reservation has to find nothing when there is
    ///     nothing to find.
    /// </remarks>
    [Fact]
    public async Task AFirstRequest_StillExecutes()
    {
        var harness = new Harness();
        var executions = 0;

        var filter = new IdempotencyEndpointFilter(null, 0, null);
        var result = await filter.InvokeAsync(harness.Invocation(), _ =>
        {
            executions++;
            return ValueTask.FromResult<object?>(Results.Text("created", statusCode: 201));
        }).ConfigureAwait(true);

        executions.Should().Be(1);
        result.Should().BeOfType<IdempotentReplayResult>();
    }

    // ── harness ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     One HTTP context and one scripted cache, with a hook that fires inside the reservation.
    /// </summary>
    private sealed class Harness
    {
        private readonly Dictionary<string, IdempotentResponse> _responses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
        private readonly DefaultHttpContext _httpContext;
        private bool _hookFired;

        /// <summary>Runs once, the first time anything reaches <c>IncrementAsync</c>.</summary>
        public Func<Task>? BeforeIncrement { get; set; }

        public Harness()
        {
            var cache = new CacheStackMock();

            cache.TryGetAsync.Returns<IdempotentResponse>(args =>
            {
                var key = (string)args[0]!;
                return new ValueTask<(bool, IdempotentResponse?)>(
                    _responses.TryGetValue(key, out var hit) ? (true, hit) : (false, null));
            });

            cache.SetAsync.Returns<IdempotentResponse>(args =>
            {
                _responses[(string)args[0]!] = (IdempotentResponse)args[1]!;
                return ValueTask.CompletedTask;
            });

            cache.IncrementAsync.Returns((string key, long delta, TimeSpan? _, CancellationToken _) =>
            {
                if (!_hookFired && BeforeIncrement is { } hook)
                {
                    _hookFired = true;
                    hook().GetAwaiter().GetResult();
                }

                _counters[key] = _counters.TryGetValue(key, out var current) ? current + delta : delta;
                return new ValueTask<long>(_counters[key]);
            });

            cache.RemoveAsync.Returns((string key, CancellationToken _) =>
            {
                _counters.Remove(key);
                return ValueTask.CompletedTask;
            });

            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ICacheStack>(cache)
                .BuildServiceProvider();

            _httpContext = new DefaultHttpContext { RequestServices = services };
            _httpContext.Request.Method = "POST";
            _httpContext.Request.Path = "/api/orders";
            _httpContext.Request.Headers["Idempotency-Key"] = "ordine-1";
        }

        public DefaultEndpointFilterInvocationContext Invocation() => new(_httpContext);
    }
}
