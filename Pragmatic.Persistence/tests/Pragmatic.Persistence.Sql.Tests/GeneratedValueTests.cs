using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     An <c>[GeneratedValue]</c> with a <c>{SEQ}</c> on the Sql engine (#562): the generator and its registration the generator now emits for the
///     engine, the sequence created on its first use and read through the session. Two saves, two numbers, in the format. Five databases.
/// </summary>
/// <remarks>Before, nothing was generated for the engine: the property reached the database empty, with no error.</remarks>
[Collection("pragmatic databases")]
public class GeneratedValueTests(PragmaticDatabases databases)
{
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Two_saves_get_two_numbers_of_the_sequence_in_the_declared_format(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes(), new QueryFilterToggle()));
        var services = new ServiceCollection();
        services.AddSingleton(session);
        services.AddPragmaticEntitiesValueGenerators(); // what the generated host calls, from the assembly's metadata
        services.AddPragmaticEntitiesComputedDefaults(); // and this, for SupportCase's [ComputedDefault] (#740): Oracle stores no empty string
        services.AddScoped(_ => new CallerDesk { Name = "Front desk", Priority = 1 });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(),
            generatedValues: scope.ServiceProvider.GetServices<IGeneratedValueBinding>());
        IRepository<SupportCase> cases = new SupportCase.SqlRepository(unitOfWork);
        var first = new SupportCase { Subject = "The lift is stuck" };
        cases.Add(first);
        await unitOfWork.SaveChangesAsync();
        var second = new SupportCase { Subject = "No hot water" };
        cases.Add(second);
        await unitOfWork.SaveChangesAsync();

        Assert.Matches(new Regex("^CASE-[0-9]{4}$"), first.Number);
        Assert.Matches(new Regex("^CASE-[0-9]{4}$"), second.Number);
        Assert.True(string.CompareOrdinal(first.Number, second.Number) < 0, $"{first.Number} then {second.Number}: the sequence counts up");
    }
}
