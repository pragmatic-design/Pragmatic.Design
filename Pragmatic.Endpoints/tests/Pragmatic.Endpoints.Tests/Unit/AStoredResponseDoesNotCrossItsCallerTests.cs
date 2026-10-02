using Pragmatic.Caching;
using Pragmatic.Endpoints.Idempotency;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     A replayed response belongs to the caller that produced it.
/// </summary>
/// <remarks>
///     <para>
///         The replay key was <c>method:path:header:bodyHash</c> and the cache behind it is a
///         singleton, so two callers sending the same <c>Idempotency-Key</c> to the same route with
///         the same body shared the stored response: the second never ran its own handler and received
///         the first one's result. Two tenants posting <c>Idempotency-Key: ordine-1</c> was enough.
///     </para>
///     <para>
///         ⚠️ The filter already named this danger, on the reservation immediately below the key:
///         <i>"sharing one run across two principals is how a de-duplicating cache turns a retry into
///         a data leak"</i>. The guard was written for the in-flight case; the replay case, three
///         lines above it, had exactly the defect the comment describes.
///     </para>
///     <para>
///         <b>Scope, stated rather than discovered.</b> Tenant and caller both enter the key when the
///         application registers something to read them from. Consequences worth knowing: the same
///         user retrying from another session still replays (same id), an anonymous retry of an
///         authenticated call re-executes, and an application that registers neither abstraction is
///         single-tenant and single-principal, where the old key was already right.
///     </para>
/// </remarks>
public class AStoredResponseDoesNotCrossItsCallerTests
{
    [Fact]
    public async Task TwoTenants_WithTheSameKey_DoNotShareAResponse()
    {
        var (filter, httpContext, tenant, _) = CreateFilter();
        httpContext.Request.Headers["Idempotency-Key"] = "ordine-1";
        var nextCalls = 0;

        EndpointFilterDelegate next = _ =>
        {
            nextCalls++;
            return ValueTask.FromResult<object?>(Results.Text($"order for {tenant.TenantId}", statusCode: 201));
        };

        tenant.TenantId = "acme";
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);

        tenant.TenantId = "globex";
        var second = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next)
            .ConfigureAwait(true);

        nextCalls.Should().Be(2,
            "the second tenant must run its own handler — replaying the first one's response hands "
            + "one tenant's created order to another");

        var replayed = await ExecuteAsync((IdempotentReplayResult)second!, httpContext.RequestServices)
            .ConfigureAwait(true);
        replayed.Body.Should().Be("order for globex");
    }

    [Fact]
    public async Task TwoUsersOfOneTenant_WithTheSameKey_DoNotShareAResponse()
    {
        var (filter, httpContext, _, user) = CreateFilter();
        httpContext.Request.Headers["Idempotency-Key"] = "ordine-1";
        var nextCalls = 0;

        EndpointFilterDelegate next = _ =>
        {
            nextCalls++;
            return ValueTask.FromResult<object?>(Results.Text($"order for {user.Id}", statusCode: 201));
        };

        user.Id = "alice";
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);

        user.Id = "bob";
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);

        nextCalls.Should().Be(2, "a response produced for one caller is not an answer for another");
    }

    /// <summary>
    ///     The control, and the reason the two above mean anything: within one tenant and one caller,
    ///     a retry still replays.
    /// </summary>
    /// <remarks>
    ///     Without it, "two tenants execute twice" is equally satisfied by a key so specific that
    ///     nothing ever replays — which would remove the feature instead of isolating it.
    /// </remarks>
    [Fact]
    public async Task TheSameCaller_Retrying_StillReplays()
    {
        var (filter, httpContext, tenant, user) = CreateFilter();
        httpContext.Request.Headers["Idempotency-Key"] = "ordine-1";
        tenant.TenantId = "acme";
        user.Id = "alice";
        var nextCalls = 0;

        EndpointFilterDelegate next = _ =>
        {
            nextCalls++;
            return ValueTask.FromResult<object?>(Results.Text("created", statusCode: 201));
        };

        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);
        var second = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next)
            .ConfigureAwait(true);

        nextCalls.Should().Be(1, "this is the same caller retrying, which is what the feature is for");

        var replayed = await ExecuteAsync((IdempotentReplayResult)second!, httpContext.RequestServices)
            .ConfigureAwait(true);
        replayed.StatusCode.Should().Be(201);
    }

    /// <summary>
    ///     An application that registers neither abstraction keeps the behaviour it had.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Single-tenant and single-principal is the shape the old key was already correct for, and
    ///     a fix that made those applications stop replaying would be a regression dressed as a
    ///     hardening.
    /// </remarks>
    [Fact]
    public async Task WithNeitherAbstractionRegistered_ARetryStillReplays()
    {
        var (filter, httpContext) = CreateBareFilter();
        httpContext.Request.Headers["Idempotency-Key"] = "ordine-1";
        var nextCalls = 0;

        EndpointFilterDelegate next = _ =>
        {
            nextCalls++;
            return ValueTask.FromResult<object?>(Results.Text("created", statusCode: 201));
        };

        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), next).ConfigureAwait(true);

        nextCalls.Should().Be(1);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private sealed class MutableTenant : ITenantContext
    {
        public string? TenantId { get; set; }
        public string? TenantName => TenantId;
        public bool IsResolved => TenantId is { Length: > 0 };
    }

    /// <summary>
    ///     Only the identity matters here; the authorization and authentication sub-objects come from
    ///     the generated mock, because this test is about which caller the key names and nothing else.
    /// </summary>
    private sealed class MutableUser : ICurrentUser
    {
        public string Id { get; set; } = "";
        public string? DisplayName => Id;
        public bool IsAuthenticated => Id.Length > 0;
        public PrincipalKind Kind => IsAuthenticated ? PrincipalKind.User : PrincipalKind.Anonymous;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        public global::Pragmatic.Authorization.IUserAuthorization Authorization { get; } =
            new UserAuthorizationMock();

        public IAuthenticationContext Authentication { get; } = new AuthenticationContextMock();
    }

    private static (IdempotencyEndpointFilter Filter, DefaultHttpContext HttpContext,
        MutableTenant Tenant, MutableUser User) CreateFilter()
    {
        var tenant = new MutableTenant();
        var user = new MutableUser();

        var httpContext = BuildContext(services =>
        {
            services.AddSingleton<ITenantContext>(tenant);
            services.AddSingleton<ICurrentUser>(user);
        });

        return (new IdempotencyEndpointFilter(null, 0, null), httpContext, tenant, user);
    }

    private static (IdempotencyEndpointFilter Filter, DefaultHttpContext HttpContext) CreateBareFilter()
        => (new IdempotencyEndpointFilter(null, 0, null), BuildContext(_ => { }));

    private static DefaultHttpContext BuildContext(Action<IServiceCollection> extra)
    {
        var store = new Dictionary<string, IdempotentResponse>(StringComparer.Ordinal);
        var counters = new Dictionary<string, long>(StringComparer.Ordinal);
        var cache = new CacheStackMock();

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

        var services = new ServiceCollection().AddLogging().AddSingleton<ICacheStack>(cache);
        extra(services);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/orders";
        return httpContext;
    }

    private static async Task<(int StatusCode, string Body)> ExecuteAsync(
        IdempotentReplayResult result, IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await result.ExecuteAsync(context).ConfigureAwait(true);

        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return (context.Response.StatusCode, await reader.ReadToEndAsync().ConfigureAwait(true));
    }
}
