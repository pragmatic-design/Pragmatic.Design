using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     The executor must read the tenant when it runs a query, not once when it is constructed.
/// </summary>
/// <remarks>
///     <para>
///         <c>EfCoreQueryExecutor</c> takes <c>ITenantContext</c> in its constructor and copies
///         <c>TenantId</c> into a field (<c>EfCoreQueryExecutor.cs:92</c>). Everything downstream uses
///         that snapshot: the <c>FilterContext</c> the filter map is built from, the tenant predicate
///         baked into it — <c>tenantId != null &amp;&amp; entity.TenantId == tenantId</c> — and the
///         cache key prefix.
///     </para>
///     <para>
///         ⚠️ So an operation that deliberately changes tenant inside a scope — which is what
///         <c>SetTenant</c> exists for — is ignored by any executor already built. The rows are the old
///         tenant's, and worse, so is the <b>cache key</b>: a query run under the switched tenant reads
///         and writes the entries of the tenant it switched away from.
///     </para>
///     <para>
///         It is also the mechanism behind the ten Showcase regressions once met here. A
///         per-scope field is written before anything in the scope is constructed, so a snapshot taken
///         at construction happens to be right; an ambient value has to be present on the <em>flow</em>
///         that constructs, and where it is not the snapshot is null — which turns the baked predicate
///         into a constant false, and a filtered search into zero rows. The two stores differ exactly
///         here, which is why this is the reading to take before anything is restored.
///     </para>
///     <para>
///         The context is stubbed rather than taken from <c>Pragmatic.MultiTenancy</c>: what is under
///         test is that the executor <em>asks again</em>, which is a property of the executor and not
///         of any one implementation of the interface.
///     </para>
/// </remarks>
public class TheExecutorReadsTheTenantOnceTests
{
    /// <summary>The claim: a tenant switch inside the scope reaches the executor.</summary>
    [Fact]
    public void ATenantChangedAfterConstruction_ReachesTheCacheKey()
    {
        var tenant = new MutableStub("acme");
        var executor = Build(tenant);

        var underAcme = executor.BuildCacheKey<Marker>("q");

        tenant.TenantId = "umbrella";

        executor.BuildCacheKey<Marker>("q").Should().NotBe(underAcme,
            "the cache is partitioned by tenant, so a query run as another tenant must not read "
            + "or write the entries of the one it switched away from");
    }

    /// <summary>
    ///     The control: with no switch, the key is the one that tenant produces, and it is the tenant's.
    /// </summary>
    /// <remarks>
    ///     Without it, "the key changes" is satisfied by a key that changes for any reason at all —
    ///     including one that stopped partitioning by tenant, which is the failure this whole family of
    ///     tests exists to prevent.
    /// </remarks>
    [Fact]
    public void WithoutASwitch_TheKeyIsTheTenantsOwn()
    {
        var acme = Build(new MutableStub("acme"));
        var umbrella = Build(new MutableStub("umbrella"));

        acme.BuildCacheKey<Marker>("q").Should().NotBe(umbrella.BuildCacheKey<Marker>("q"));
        acme.BuildCacheKey<Marker>("q").Should().Contain("acme");
    }

    /// <summary>The second control: switching back puts the key back.</summary>
    /// <remarks>
    ///     A one-way read would satisfy the claim above and still be wrong: the restore at the end of a
    ///     <c>SetTenant</c> has to be followed too, or an operation leaves its scope contaminated for
    ///     everything that runs after it.
    /// </remarks>
    [Fact]
    public void SwitchingBack_RestoresTheKey()
    {
        var tenant = new MutableStub("acme");
        var executor = Build(tenant);

        var underAcme = executor.BuildCacheKey<Marker>("q");

        tenant.TenantId = "umbrella";
        tenant.TenantId = "acme";

        executor.BuildCacheKey<Marker>("q").Should().Be(underAcme);
    }

    private static EfCoreQueryExecutor Build(ITenantContext tenant) =>
        new(
            filterProvider: null,
            filterMapComposer: null,
            filterToggle: null,
            cacheStack: null,
            logger: null,
            tenantContext: tenant,
            currentUser: null);

    /// <summary>A tenant context whose value changes, which is what every real one does.</summary>
    private sealed class MutableStub(string? tenantId) : ITenantContext
    {
        public string? TenantId { get; set; } = tenantId;

        public string? TenantName => null;

        public bool IsResolved => !string.IsNullOrEmpty(TenantId);
    }

    private sealed class Marker;
}
