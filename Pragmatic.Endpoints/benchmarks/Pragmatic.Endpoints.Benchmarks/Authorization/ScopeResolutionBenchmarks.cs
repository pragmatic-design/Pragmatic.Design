using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pragmatic.Authorization;
using Pragmatic.Authorization.Scopes;
using Pragmatic.Identity;

namespace Pragmatic.Endpoints.Benchmarks.Authorization;

/// <summary>
///     What resolving the caller's data scopes costs a request whose queries go through generated data filters
///     (#136): each filter calls <see cref="IUserScopeResolver.ResolveAccessScopes" /> once per query.
/// </summary>
/// <remarks>
///     <para>
///         <c>Blocking</c> is the behaviour before #136, reproduced: the interface's former default ran the
///         asynchronous method with <c>Task.Run</c> and blocked on it, and nothing was kept between queries.
///         <c>Direct</c> is <see cref="DefaultUserScopeResolver" /> as it is now: synchronous, and once per
///         resolver, which is scoped to the request.
///     </para>
///     <para>
///         A request is a fresh resolver and <see cref="QueriesPerRequest" /> queries; the concurrent rows run
///         <see cref="ConcurrentRequests" /> requests at once on the thread pool, as a host under load does.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ScopeResolutionBenchmarks
{
    private const int QueriesPerRequest = 3;
    private const int ConcurrentRequests = 64;

    private readonly ICurrentUser _user = new BenchmarkUser();

    [Benchmark(Baseline = true), BenchmarkCategory("OneRequest")]
    public int Scopes_Blocking_OneRequest() => Request(new BlockingResolver());

    [Benchmark, BenchmarkCategory("OneRequest")]
    public int Scopes_Direct_OneRequest() => Request(new DefaultUserScopeResolver());

    [Benchmark(Baseline = true), BenchmarkCategory("ConcurrentRequests")]
    public void Scopes_Blocking_ConcurrentRequests() => Concurrently(() => Request(new BlockingResolver()));

    [Benchmark, BenchmarkCategory("ConcurrentRequests")]
    public void Scopes_Direct_ConcurrentRequests() => Concurrently(() => Request(new DefaultUserScopeResolver()));

    // A fixed number of pool work items rather than Parallel.For, whose allocations follow how many workers it
    // took: the allocation ratchet compares this row byte for byte between runs.
    private static void Concurrently(Func<int> request)
    {
        var requests = new Task[ConcurrentRequests];
        for (var i = 0; i < requests.Length; i++)
            requests[i] = Task.Run(request);
        Task.WaitAll(requests);
    }

    private int Request(IUserScopeResolver resolver)
    {
        var count = 0;
        for (var query = 0; query < QueriesPerRequest; query++)
            count += resolver.ResolveAccessScopes(_user).Count;
        return count;
    }

    // The former interface default, over the expansion DefaultUserScopeResolver performs: a fresh resolver per
    // call, so nothing is kept between queries, as nothing was.
    private sealed class BlockingResolver : IUserScopeResolver
    {
        public Task<IReadOnlySet<string>> ResolveAccessScopesAsync(ICurrentUser user, CancellationToken ct = default)
            => Task.FromResult(new DefaultUserScopeResolver().ResolveAccessScopes(user));

        public IReadOnlySet<string> ResolveAccessScopes(ICurrentUser user)
            => Task.Run(() => ResolveAccessScopesAsync(user)).GetAwaiter().GetResult();
    }

    private sealed class BenchmarkUser : ICurrentUser
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _claims = new() { ["data-scope"] = ["north", "south"] };

        public string Id => "u-42";
        public string? DisplayName => "User";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => _claims;
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }
}
