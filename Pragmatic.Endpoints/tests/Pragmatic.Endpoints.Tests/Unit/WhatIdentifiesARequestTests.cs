using System.Text;
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
///     What the replay key counts as "the same request".
/// </summary>
/// <remarks>
///     <para>
///         The body was hashed only when <c>Content-Length</c> was set: `ContentLength is null or 0`
///         answered <c>"nobody"</c>. A chunked request has no declared length — it is the ordinary
///         shape for any client that streams — so two different bodies sent with one key produced the
///         <b>same</b> key, and the second replayed the first's response.
///     </para>
///     <para>
///         ⚠️ The remark above the hashing explains why the body is in the key at all: <i>"two textually
///         different but semantically equal bodies re-execute rather than replay — the conservative
///         direction"</i>. That branch was the opposite: two genuinely different bodies collapsed into
///         one. The form case is a deliberate trade and says so; this one only looked like the same
///         trade.
///     </para>
///     <para>
///         The query string was absent from the key too, and it is in now for the reason the body is:
///         <c>?range=today</c> and <c>?range=month</c> are different operations, and re-executing is
///         the conservative direction. That is this file's own rule applied, not a new one.
///     </para>
/// </remarks>
public class WhatIdentifiesARequestTests
{
    [Fact]
    public async Task TwoChunkedBodies_WithOneKey_BothExecute()
    {
        var harness = new Harness();
        var executions = 0;
        EndpointFilterDelegate endpoint = _ => { executions++; return Replied(); };

        await harness.InvokeAsync(endpoint, body: "{\"amount\":10}", chunked: true).ConfigureAwait(true);
        await harness.InvokeAsync(endpoint, body: "{\"amount\":9999}", chunked: true).ConfigureAwait(true);

        executions.Should().Be(2,
            "the bodies differ, and a request whose length the client did not declare is still a "
            + "request with a body — replaying the first answer here charges the wrong amount");
    }

    /// <summary>
    ///     The control: the same chunked body with the same key still replays.
    /// </summary>
    /// <remarks>
    ///     Without it, "different chunked bodies execute twice" is equally satisfied by a key so
    ///     specific that nothing ever replays — which removes the feature instead of fixing it.
    /// </remarks>
    [Fact]
    public async Task TheSameChunkedBody_WithOneKey_Replays()
    {
        var harness = new Harness();
        var executions = 0;
        EndpointFilterDelegate endpoint = _ => { executions++; return Replied(); };

        await harness.InvokeAsync(endpoint, body: "{\"amount\":10}", chunked: true).ConfigureAwait(true);
        await harness.InvokeAsync(endpoint, body: "{\"amount\":10}", chunked: true).ConfigureAwait(true);

        executions.Should().Be(1, "this is the retry the filter exists for");
    }

    [Fact]
    public async Task TwoQueryStrings_WithOneKey_BothExecute()
    {
        var harness = new Harness();
        var executions = 0;
        EndpointFilterDelegate endpoint = _ => { executions++; return Replied(); };

        await harness.InvokeAsync(endpoint, query: "?range=today").ConfigureAwait(true);
        await harness.InvokeAsync(endpoint, query: "?range=month").ConfigureAwait(true);

        executions.Should().Be(2, "a different range is a different operation, whatever key carried it");
    }

    /// <summary>The control for the query string, for the same reason as the body's.</summary>
    [Fact]
    public async Task TheSameQueryString_WithOneKey_Replays()
    {
        var harness = new Harness();
        var executions = 0;
        EndpointFilterDelegate endpoint = _ => { executions++; return Replied(); };

        await harness.InvokeAsync(endpoint, query: "?range=today").ConfigureAwait(true);
        await harness.InvokeAsync(endpoint, query: "?range=today").ConfigureAwait(true);

        executions.Should().Be(1);
    }

    /// <summary>
    ///     ⚠️ A form post keeps the behaviour it documents: the key and the route identify it.
    /// </summary>
    /// <remarks>
    ///     That branch is a trade the remark states — a form is read as a form, and buffering an upload
    ///     to hash it is a poor bargain. Changing it under cover of this fix would be widening the
    ///     scope into the one case that was on purpose.
    /// </remarks>
    [Fact]
    public async Task TwoFormPosts_WithOneKey_StillReplay()
    {
        var harness = new Harness();
        var executions = 0;
        EndpointFilterDelegate endpoint = _ => { executions++; return Replied(); };

        await harness.InvokeAsync(endpoint, body: "a=1", contentType: "application/x-www-form-urlencoded")
            .ConfigureAwait(true);
        await harness.InvokeAsync(endpoint, body: "a=2", contentType: "application/x-www-form-urlencoded")
            .ConfigureAwait(true);

        executions.Should().Be(1, "documented: the key plus the route identify a form request");
    }

    private static ValueTask<object?> Replied()
        => ValueTask.FromResult<object?>(Results.Text("created", statusCode: 201));

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
                _counters[key] = _counters.TryGetValue(key, out var current) ? current + delta : delta;
                return new ValueTask<long>(_counters[key]);
            });

            cache.RemoveAsync.Returns((string key, CancellationToken _) =>
            {
                _counters.Remove(key);
                return ValueTask.CompletedTask;
            });

            _services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ICacheStack>(cache)
                .BuildServiceProvider();
        }

        /// <summary>
        ///     One request through the filter.
        /// </summary>
        /// <remarks>
        ///     ⚠️ <c>chunked</c> leaves <c>Content-Length</c> unset, which is what a streamed request
        ///     looks like — and the shape a filter reading <c>Content-Length</c> would take for "no body
        ///     at all".
        /// </remarks>
        public async Task<object?> InvokeAsync(
            EndpointFilterDelegate endpoint,
            string? body = null,
            bool chunked = false,
            string? query = null,
            string contentType = "application/json")
        {
            var httpContext = new DefaultHttpContext { RequestServices = _services };
            httpContext.Request.Method = "POST";
            httpContext.Request.Path = "/api/orders";
            httpContext.Request.Headers["Idempotency-Key"] = "ordine-1";

            if (query is not null)
                httpContext.Request.QueryString = new QueryString(query);

            if (body is not null)
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                httpContext.Request.Body = new MemoryStream(bytes);
                httpContext.Request.ContentType = contentType;
                if (!chunked) httpContext.Request.ContentLength = bytes.Length;
            }

            var filter = new IdempotencyEndpointFilter(null, 0, null);
            return await filter
                .InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), endpoint)
                .ConfigureAwait(true);
        }
    }
}
