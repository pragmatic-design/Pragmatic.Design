using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The Sql engine's save runs an entity's <see cref="IEntityLifecycle{T}.OnSaving" /> hook, whatever wrote it (#644): here a repository and
///     a save with no mutation above them. Five databases.
/// </summary>
/// <remarks>
///     The hook ran only in the mutation invoker, so a value derived on save was stale on every other path. The hook below derives the name's
///     canonical form, as an application's derivations do.
/// </remarks>
[Collection("pragmatic databases")]
public class TheSaveRunsTheOnSavingHooksTests(PragmaticDatabases databases)
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

    private static (EntitySavingHooksRunner Runner, UpperCaseName Hook, ServiceProvider Services) Hooks()
    {
        var hook = new UpperCaseName();
        var services = new ServiceCollection().AddSingleton<IEntityLifecycle<Property>>(hook).BuildServiceProvider();
        return (new EntitySavingHooksRunner(services, [new EntitySavingHooks<Property>()]), hook, services);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task An_added_entity_is_saved_with_what_its_hook_derived(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var (runner, hook, services) = Hooks();
        using var _ = services;
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(), savingHooks: runner);

        var lago = new Property { Name = "lago" };
        ((IRepository<Property>)new Property.SqlRepository(unitOfWork)).Add(lago);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal(1, hook.Calls);
        Assert.Contains(await UnitOfWorkTests.RawAsync<Property>(session, _toggle), p => p.Id == lago.Id && p.Name == "LAGO");
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_changed_entity_is_saved_with_what_its_hook_derived(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var riva = (await UnitOfWorkTests.RawAsync<Property>(session, _toggle)).Single(p => p.Name == "RIVA");
        var (runner, _, services) = Hooks();
        using var __ = services;
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(), savingHooks: runner);

        var loaded = (await ((IRepository<Property>)new Property.SqlRepository(unitOfWork)).GetByIdAsync(riva.Id))!;
        loaded.Name = "riva sul lago";
        await unitOfWork.SaveChangesAsync();

        Assert.Equal("RIVA SUL LAGO", (await UnitOfWorkTests.RawAsync<Property>(session, _toggle)).Single(p => p.Id == riva.Id).Name);
    }

    /// <summary>The control: without the hooks the name is saved as written.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Without_hooks_the_entity_is_saved_as_written(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());

        var lago = new Property { Name = "lago" };
        ((IRepository<Property>)new Property.SqlRepository(unitOfWork)).Add(lago);
        await unitOfWork.SaveChangesAsync();

        Assert.Contains(await UnitOfWorkTests.RawAsync<Property>(session, _toggle), p => p.Id == lago.Id && p.Name == "lago");
    }
}
