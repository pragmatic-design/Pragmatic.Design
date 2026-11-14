using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A <c>[ComputedDefault]</c> is filled at the save, for an entity added through a repository as a domain action adds one, by a generator
///     that takes a scoped service (#740). Five databases.
/// </summary>
/// <remarks>
///     <para>
///         The generator was a singleton resolved by the create mutation's invoker: the container refused one that took a scoped service, and
///         an entity added any other way reached the database with the property empty. Both are measured here: the registration is validated
///         with <c>ValidateScopes</c>, and the entity is added through its repository, with no mutation anywhere.
///     </para>
///     <para>
///         The control: a value the caller gave is kept, as for a <c>[GeneratedValue]</c>.
///     </para>
/// </remarks>
[Collection("pragmatic databases")]
public class AComputedDefaultIsFilledAtTheSaveTests(PragmaticDatabases databases)
{
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task An_added_case_gets_the_desk_of_the_caller_from_a_scoped_service(SqlDialect dialect)
    {
        var (session, provider, scope, unitOfWork, cases) = await OpenAsync(dialect, "Front desk", 3);
        using var heldSession = session;
        await using var heldProvider = provider;
        await using var heldScope = scope;

        var opened = new SupportCase { Subject = "The lift is stuck" };
        cases.Add(opened);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal("Front desk", opened.Desk);
        Assert.Equal(3, opened.Priority);
        var stored = (await session.Query<SupportCase>().ToListAsync()).Single(c => c.Id == opened.Id);
        Assert.Equal(("Front desk", 3), (stored.Desk, stored.Priority));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_desk_the_caller_gave_is_kept(SqlDialect dialect)
    {
        var (session, provider, scope, unitOfWork, cases) = await OpenAsync(dialect, "Front desk", 3);
        using var heldSession = session;
        await using var heldProvider = provider;
        await using var heldScope = scope;

        var opened = new SupportCase { Subject = "No hot water", Desk = "Night porter" };
        cases.Add(opened);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal("Night porter", opened.Desk);
    }

    private async Task<(SqlSession, ServiceProvider, AsyncServiceScope, SqlUnitOfWork, IRepository<SupportCase>)> OpenAsync(
        SqlDialect dialect, string desk, int priority)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes(), new QueryFilterToggle()));
        var services = new ServiceCollection();
        services.AddSingleton(session);
        services.AddScoped<CallerDesk>();
        services.AddPragmaticEntitiesValueGenerators();
        services.AddPragmaticEntitiesComputedDefaults(); // what the generated host calls, from the assembly's metadata
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateAsyncScope();
        var caller = scope.ServiceProvider.GetRequiredService<CallerDesk>();
        caller.Name = desk;
        caller.Priority = priority;

        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(),
            generatedValues: scope.ServiceProvider.GetServices<IGeneratedValueBinding>());
        return (session, provider, scope, unitOfWork, new SupportCase.SqlRepository(unitOfWork));
    }
}
