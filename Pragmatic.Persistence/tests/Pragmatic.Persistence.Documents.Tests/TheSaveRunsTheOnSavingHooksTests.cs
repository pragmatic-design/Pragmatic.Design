using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Query.Filters;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The documents engine's save runs an entity's <see cref="IEntityLifecycle{T}.OnSaving" /> hook, whatever wrote it (#644): here a
///     repository and a save with no mutation above them. MongoDB (a replica set) and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class TheSaveRunsTheOnSavingHooksTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private sealed class UpperCaseName : IEntityLifecycle<Property>
    {
        public int Calls { get; private set; }

        public void OnSaving(Property entity, LifecycleContext context)
        {
            Calls++;
            entity.Name = entity.Name.ToUpperInvariant();
        }
    }

    private static DocUnitOfWork UnitOfWork(DocSession session, EntitySavingHooksRunner? hooks)
        => new(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(), savingHooks: hooks);

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task An_added_document_is_saved_with_what_its_hook_derived(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await UnitOfWorkTests.AnnaAsync(servers, store, _toggle);
        var hook = new UpperCaseName();
        using var services = new ServiceCollection().AddSingleton<IEntityLifecycle<Property>>(hook).BuildServiceProvider();
        var unitOfWork = UnitOfWork(session, new EntitySavingHooksRunner(services, [new EntitySavingHooks<Property>()]));

        var lago = new Property { Name = "lago" };
        new Property.DocRepository(unitOfWork).Add(lago);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal(1, hook.Calls);
        Assert.Equal("LAGO", (await UnitOfWorkTests.PropertiesAsync(session, _toggle)).Single(p => p.Id == lago.Id).Name);
    }

    /// <summary>The control: without the hooks the name is saved as written.</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Without_hooks_the_document_is_saved_as_written(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await UnitOfWorkTests.AnnaAsync(servers, store, _toggle);
        var unitOfWork = UnitOfWork(session, null);

        var lago = new Property { Name = "lago" };
        new Property.DocRepository(unitOfWork).Add(lago);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal("lago", (await UnitOfWorkTests.PropertiesAsync(session, _toggle)).Single(p => p.Id == lago.Id).Name);
    }
}
