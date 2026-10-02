using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.Tests.Entity;

/// <summary>
///     A lookup cache belongs to the tenant it was loaded for.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ One cache per <see cref="Type" />, process wide, would let whichever tenant was preloaded
///         last answer for all of them — and a generated navigation on an entity reads through
///         <see cref="LookupResolver" />, with no scope to ask.
///     </para>
///     <para>
///         The tenant comes from the ambient <see cref="TenantScope" /> for the reason the resolver is
///         static in the first place: a generated navigation property lives on an entity instance,
///         which has no service provider to resolve anything from. That is why the ambient is forced
///         rather than chosen.
///     </para>
///     <para>
///         A lookup that is not tenant-scoped registers under no tenant — the shared slot — and is
///         readable from inside a tenant too, because most lookup tables hold the same
///         rows for everyone. A tenant's own rows win over that shared slot, never the reverse.
///     </para>
/// </remarks>
[Collection(LookupResolverCollection.Name)]
public class LookupResolverTenantTests : IDisposable
{
    public LookupResolverTenantTests() => LookupResolver.Reset();

    public void Dispose()
    {
        LookupResolver.Reset();
        GC.SuppressFinalize(this);
    }

    /// <summary>The claim: each tenant reads the rows loaded for it.</summary>
    [Fact]
    public void TwoTenants_EachReadsItsOwnCache()
    {
        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Register<Status, int>(new StubCache(new Status("open in A")));

        using (TenantScope.BeginScope("tenant-b"))
            LookupResolver.Register<Status, int>(new StubCache(new Status("open in B")));

        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Get<Status, int>(1).Name.Should().Be("open in A");

        using (TenantScope.BeginScope("tenant-b"))
            LookupResolver.Get<Status, int>(1).Name.Should().Be("open in B");
    }

    /// <summary>
    ///     The control: a lookup registered under no tenant still answers under no tenant.
    /// </summary>
    /// <remarks>
    ///     Without it, "partition by tenant" is satisfied by a resolver that answers nothing at all
    ///     when there is no tenant — which is every application that does not use multi-tenancy.
    /// </remarks>
    [Fact]
    public void WithNoTenant_TheResolverAnswersAsItAlwaysDid()
    {
        LookupResolver.Register<Status, int>(new StubCache(new Status("shared")));

        LookupResolver.Get<Status, int>(1).Name.Should().Be("shared");
        LookupResolver.TryGet<Status, int>(1, out var found).Should().BeTrue();
        found!.Name.Should().Be("shared");
    }

    /// <summary>
    ///     And the second control: one tenant's rows are not visible to another, nor to no tenant.
    /// </summary>
    /// <remarks>
    ///     This is the failure the issue describes — "for a per-tenant table the same shape would serve
    ///     one tenant's row to another" — asked directly rather than inferred from the first case.
    /// </remarks>
    [Fact]
    public void ATenantsCache_IsNotVisibleToAnotherTenant()
    {
        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Register<Status, int>(new StubCache(new Status("only A")));

        using (TenantScope.BeginScope("tenant-b"))
            LookupResolver.TryGet<Status, int>(1, out _).Should()
                .BeFalse("tenant-b was never preloaded, and reading nothing is the fail-closed answer");

        LookupResolver.TryGet<Status, int>(1, out _).Should()
            .BeFalse("and neither does a caller outside every tenant");
    }

    /// <summary>
    ///     A lookup that is not tenant-scoped is readable from inside a tenant.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The case the Showcase found, and the reason the resolver falls back to the shared slot.
    ///     Most lookup tables — countries, statuses, categories — hold the same rows for everyone and are
    ///     preloaded once under no tenant; without the fallback, partitioning by tenant would have made
    ///     every one of them unreadable the moment a request resolved a tenant.
    /// </remarks>
    [Fact]
    public void ASharedLookup_IsReadableFromInsideATenant()
    {
        LookupResolver.Register<Status, int>(new StubCache(new Status("shared")));

        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Get<Status, int>(1).Name.Should().Be("shared");
    }

    /// <summary>
    ///     And the tenant's own rows win over the shared ones, never the reverse.
    /// </summary>
    /// <remarks>
    ///     Without this, the fallback is satisfied by a resolver that always answers from the shared
    ///     slot — which is the process-wide cache this issue exists to end, with an extra step.
    /// </remarks>
    [Fact]
    public void ATenantsOwnRows_WinOverTheSharedOnes()
    {
        LookupResolver.Register<Status, int>(new StubCache(new Status("shared")));

        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Register<Status, int>(new StubCache(new Status("only A")));

        using (TenantScope.BeginScope("tenant-a"))
            LookupResolver.Get<Status, int>(1).Name.Should().Be("only A");

        using (TenantScope.BeginScope("tenant-b"))
            LookupResolver.Get<Status, int>(1).Name.Should().Be("shared");
    }

    private sealed record Status(string Name);

    private sealed class StubCache(Status only) : ILookupCache<Status, int>
    {
        public Status Get(int id) => only;

        public bool TryGet(int id, out Status? value)
        {
            value = only;
            return true;
        }

        public IReadOnlyList<Status> GetAll() => [only];
    }
}
