using Pragmatic.Authorization.Evaluation;
using Pragmatic.Identity;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Authorization.Tests.Unit;

/// <summary>
///     A provider that asks for the permissions it is itself resolving gets an empty set, not a
///     stack overflow.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ This is not hypothetical. <see cref="IPermissionProvider" /> documents reading from "an
///         external source (JWT claims, database, policy server)", and a per-tenant permission store
///         has to read the application database. Going through a repository evaluates the query
///         filters, a permission-based filter asks the caller's permissions, and resolution starts
///         again — the resolver assigns its cache only after the providers finish, so nothing broke
///         the cycle. A consumer application hit exactly this and the test host died: <i>"Arresto
///         anomalo del processo host di test: Stack overflow"</i>, with no exception to catch and
///         nothing in the log.
///     </para>
///     <para>
///         What the guard buys is a defined answer, not a working read: a provider that needs its own
///         data still has to read it past the filter pipeline. What it removes is a crash whose cause
///         is invisible.
///     </para>
/// </remarks>
public class CachedPermissionResolverReentrancyTests
{
    /// <summary>A provider that asks the resolver for permissions while resolving them.</summary>
    private sealed class ReentrantProvider(string permission) : IPermissionProvider
    {
        public CachedPermissionResolver? Resolver { get; set; }

        /// <summary>What the resolver handed back on the way in — empty if the guard held.</summary>
        public IReadOnlySet<string>? SawFromInside { get; private set; }

        public int Order => 0;

        public ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
            ICurrentUser user, CancellationToken ct = default)
        {
            SawFromInside = Resolver!.Permissions;

            return ValueTask.FromResult<IReadOnlySet<string>>(
                new HashSet<string>([permission], StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed class TestUser : ICurrentUser
    {
        public string Id => "test";
        public string? DisplayName => "Test";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
            => new Dictionary<string, IReadOnlyList<string>>();

        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    [Fact]
    public void ResolvePermissions_WhenAProviderAsksForThemAgain_ReturnsEmptyInsteadOfRecursing()
    {
        var provider = new ReentrantProvider("orders.read");
        var resolver = new CachedPermissionResolver([provider], new TestUser());
        provider.Resolver = resolver;

        // Without the guard this line never returns: the process dies of a stack overflow.
        var permissions = resolver.Permissions;

        provider.SawFromInside.Should().NotBeNull("the provider ran and asked from inside");
        provider.SawFromInside!.Should().BeEmpty("a reentrant ask gets nothing, and gets it quickly");

        // The control: the reentrancy did not poison the real answer. Without this the test would
        // also pass if the guard made the whole resolution return empty.
        permissions.Should().ContainSingle().Which.Should().Be("orders.read");
    }

    [Fact]
    public void ResolvePermissions_AfterAReentrantResolution_StillAnswersFromTheCache()
    {
        var provider = new ReentrantProvider("orders.read");
        var resolver = new CachedPermissionResolver([provider], new TestUser());
        provider.Resolver = resolver;

        _ = resolver.Permissions;

        // ⚠️ The guard must not leave the resolver stuck: the empty set is deliberately NOT cached,
        // so the second ask is a cache hit on the real answer rather than a second empty one.
        resolver.Permissions.Should().ContainSingle().Which.Should().Be("orders.read");
        resolver.HasPermission("orders.read").Should().BeTrue();
    }
}
