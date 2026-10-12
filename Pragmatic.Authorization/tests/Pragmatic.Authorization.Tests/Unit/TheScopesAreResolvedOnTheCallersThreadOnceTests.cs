using Pragmatic.Authorization;
using Pragmatic.Authorization.Scopes;
using Pragmatic.Identity;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Authorization.Tests.Unit;

/// <summary>
///     The synchronous scope resolution every generated data filter calls runs on the caller's thread, and once
///     per request (#136).
/// </summary>
/// <remarks>
///     <para>
///         The interface's default used to be <c>Task.Run(...).GetAwaiter().GetResult()</c>, and the default
///         resolver did not override it: every query over a scoped entity queued a work item and blocked the
///         calling thread on it, two pool threads for a computation with nothing to wait for.
///     </para>
///     <para>
///         The call is made from a dedicated thread, not from a pool thread. A pool thread waiting on a task it
///         queued itself may run that task inline, and the test would then see the caller's thread for the wrong
///         reason.
///     </para>
/// </remarks>
public class TheScopesAreResolvedOnTheCallersThreadOnceTests
{
    [Fact]
    public void ResolveAccessScopes_RunsOnTheCallersThread()
    {
        var user = new RecordingUser();
        IUserScopeResolver resolver = new DefaultUserScopeResolver();

        var callerThread = 0;
        IReadOnlySet<string>? scopes = null;
        var caller = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            scopes = resolver.ResolveAccessScopes(user);
        });
        caller.Start();
        caller.Join();

        user.ReadOnThreads.Should().Equal(callerThread);
        scopes.Should().Contain(ScopeIdentifiers.ForUser("u-1"));
    }

    [Fact]
    public void ResolveAccessScopes_ResolvesOncePerResolver()
    {
        var user = new RecordingUser();
        IUserScopeResolver resolver = new DefaultUserScopeResolver();

        var first = resolver.ResolveAccessScopes(user);
        var second = resolver.ResolveAccessScopes(user);

        second.Should().BeSameAs(first, "the resolver is scoped: one request, one resolution");
        user.ReadOnThreads.Should().HaveCount(1);
    }

    [Fact]
    public async Task ResolveAccessScopesAsync_AnswersWhatTheSynchronousOneDoes()
    {
        var user = new RecordingUser();
        IUserScopeResolver resolver = new DefaultUserScopeResolver();

        var asynchronous = await resolver.ResolveAccessScopesAsync(user);

        asynchronous.Should().BeSameAs(resolver.ResolveAccessScopes(user));
        asynchronous.Should().Contain(ScopeIdentifiers.ForScope("north"));
    }

    // Records the thread its claims are read on: reading them is what resolving the scopes does.
    private sealed class RecordingUser : ICurrentUser
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _claims = new() { ["data-scope"] = ["north"] };

        public List<int> ReadOnThreads { get; } = [];

        public string Id => "u-1";
        public string? DisplayName => "User";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
        {
            get
            {
                lock (ReadOnThreads)
                    ReadOnThreads.Add(Environment.CurrentManagedThreadId);
                return _claims;
            }
        }

        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }
}
