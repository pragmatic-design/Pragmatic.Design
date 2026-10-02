using Pragmatic.Agent.Client.Stores;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Agent.Tests.Stores;

/// <summary>
///     <c>ITenantStore.CreateAsync</c> says implementations "must throw an
///     <see cref="InvalidOperationException"/> rather than silently updating or ignoring the
///     duplicate, to preserve data integrity". Two of the three implementations did neither: they
///     assigned through an indexer, so creating a tenant that already existed replaced one tenant's
///     metadata with another's and reported success.
/// </summary>
/// <remarks>
///     A contract sentence with no test is a wish. Only <c>InMemoryTenantStore</c> honoured it, and
///     nothing compared the three.
/// </remarks>
public class TenantStoreContractTests
{
    private static TenantInfo Tenant(string id, string name) => new()
    {
        TenantId = id,
        TenantName = name,
        State = TenantState.Active,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task Create_WithADuplicateId_IsRefused()
    {
        var store = new InProcessTenantStore();
        await store.CreateAsync(Tenant("acme", "Acme"));

        var act = () => store.CreateAsync(Tenant("acme", "Someone Else"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>The point of the refusal: the first tenant's data is still there.</summary>
    [Fact]
    public async Task Create_WithADuplicateId_LeavesTheFirstTenantIntact()
    {
        var store = new InProcessTenantStore();
        await store.CreateAsync(Tenant("acme", "Acme"));

        try
        {
            await store.CreateAsync(Tenant("acme", "Someone Else"));
        }
        catch (InvalidOperationException)
        {
            // The refusal is asserted above; here we care about what survived it.
        }

        (await store.GetByIdAsync("acme"))!.TenantName.Should().Be("Acme");
    }

    [Fact]
    public async Task Update_OnAnUnknownTenant_ReportsFalse()
    {
        var store = new InProcessTenantStore();

        (await store.UpdateAsync(Tenant("ghost", "Ghost"))).Should().BeFalse();
    }

    /// <summary>
    ///     And does not create it on the way: returning false while writing the row would be the
    ///     same lie in the other direction.
    /// </summary>
    [Fact]
    public async Task Update_OnAnUnknownTenant_DoesNotCreateIt()
    {
        var store = new InProcessTenantStore();

        await store.UpdateAsync(Tenant("ghost", "Ghost"));

        (await store.GetByIdAsync("ghost")).Should().BeNull();
    }

    [Fact]
    public async Task Update_OnAnExistingTenant_AppliesAndReportsTrue()
    {
        var store = new InProcessTenantStore();
        await store.CreateAsync(Tenant("acme", "Acme"));

        (await store.UpdateAsync(Tenant("acme", "Acme Renamed"))).Should().BeTrue();
        (await store.GetByIdAsync("acme"))!.TenantName.Should().Be("Acme Renamed");
    }
}
