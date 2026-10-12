using Pragmatic.Actions.Pipeline;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     The permission requirement of an operation is the one the calling host's registry declares, and a
///     check whose requirement is already known allocates nothing (#140).
/// </summary>
/// <remarks>
///     The filter cached requirements in a static dictionary keyed by type alone, so the first registry to ask
///     about a type decided it for every host in the process: two test hosts, a host and its integration tests,
///     a modular monolith that builds more than one container. The lookup also captured the registry in a
///     closure, allocated on every check, the cached ones included.
/// </remarks>
public class APermissionRequirementIsCachedPerRegistryTests
{
    // One type per test: the requirement cache outlives a test, and a type shared with another test would
    // measure that test's registry.
    private sealed class GuardedInOneHost;

    private sealed class GuardedInTheOtherHost;

    private sealed class NeverGuarded;

    private sealed class Registry(PermissionRequirementEntry? entry) : IPermissionRequirementRegistry
    {
        public PermissionRequirementEntry? GetRequirement(Type actionType) => entry;
    }

    private static readonly Registry Guarding = new(new PermissionRequirementEntry(["orders.read"], RequireAll: true));
    private static readonly Registry Open = new(null);

    [Fact]
    public void TwoHosts_AskedGuardingFirst_EachGetsItsOwnRequirement()
    {
        Check<GuardedInOneHost>(Guarding).IsSuccess.Should().BeFalse("this host's registry requires orders.read");
        Check<GuardedInOneHost>(Open).IsSuccess.Should().BeTrue("the other host's registry requires nothing");
    }

    [Fact]
    public void TwoHosts_AskedOpenFirst_EachGetsItsOwnRequirement()
    {
        Check<GuardedInTheOtherHost>(Open).IsSuccess.Should().BeTrue("this host's registry requires nothing");
        Check<GuardedInTheOtherHost>(Guarding).Error.Should().BeOfType<UnauthorizedError>(
            "the other host's registry requires orders.read, and the caller is anonymous");
    }

    [Fact]
    public void ACachedCheck_AllocatesNothing()
    {
        Check<NeverGuarded>(Open);   // fills the cache

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
            Check<NeverGuarded>(Open);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0L, "a requirement already resolved is read, not resolved again");
    }

    private static VoidResult<IError> Check<T>(IPermissionRequirementRegistry registry)
        => PermissionAuthorizationFilter.CheckPermissionsFor(typeof(T), AnonymousUser.Instance, isInternalCall: false, registry);
}
