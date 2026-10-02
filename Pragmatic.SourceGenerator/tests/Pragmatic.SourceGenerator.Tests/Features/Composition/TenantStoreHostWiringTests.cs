using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     The generated host registers the tenant store wrapped, so the tenant lifecycle reaches its
///     observers.
/// </summary>
/// <remarks>
///     ⚠️ An instance built outside the container, such as a bare <c>new InMemoryTenantStore()</c>,
///     cannot be given anything from it, so every <c>ITenantLifecycleObserver</c> an application or a
///     module registers would be unreachable, and anything derived from the set of tenants would go
///     stale in silence.
/// </remarks>
public class TenantStoreHostWiringTests
{
    private const string HostStubs = """
        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }
        namespace Pragmatic.MultiTenancy
        {
            public class InMemoryTenantStore { }
        }
        public static class Program
        {
            public static void Main() { }
        }
        """;

    /// <summary>The store is built from the container, with the observers it finds there.</summary>
    [Fact]
    public void AHostWithMultiTenancy_WrapsTheTenantStoreSoObserversAreReached()
    {
        var host = GetHostServices();

        host.Should().NotBeNull("a host referencing Pragmatic.MultiTenancy registers a tenant store");
        host!.Should().Contain("new global::Pragmatic.MultiTenancy.ObservedTenantStore(")
            .And.Contain("sp.GetServices<global::Pragmatic.MultiTenancy.ITenantLifecycleObserver>()");
    }

    /// <summary>
    ///     The control: it is not registered as an instance built outside the container.
    /// </summary>
    /// <remarks>
    ///     Without it, "the store is wrapped" is satisfied by a host that registers both — the bare
    ///     instance last, which is the one the container would hand out.
    /// </remarks>
    [Fact]
    public void TheTenantStore_IsNoLongerABareInstance()
    {
        var host = GetHostServices();

        host!.Should().NotContain(
            "AddSingleton<global::Pragmatic.MultiTenancy.ITenantStore>("
            + "new global::Pragmatic.MultiTenancy.InMemoryTenantStore());");
    }

    private static string? GetHostServices()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(HostStubs, []);
        return GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Host.Services"))
            .Select(kv => kv.Value)
            .FirstOrDefault();
    }
}
