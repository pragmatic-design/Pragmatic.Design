using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Repository;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     An <c>[GeneratedValue]</c> with a <c>{SEQ}</c> on the documents engine (#562): the generator and its registration the generator now emits
///     for the engine, the counter document created on its first use and incremented by the server. Two saves, two numbers, in the format.
///     MongoDB and Cosmos DB.
/// </summary>
/// <remarks>Before, nothing was generated for the engine: the property reached the database empty, with no error.</remarks>
[Collection("document servers")]
public class GeneratedValueTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Two_saves_get_two_numbers_of_the_counter_in_the_declared_format(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
            await session.EnsureContainerAsync<SupportCase>();
        var services = new ServiceCollection();
        services.AddSingleton(session);
        services.AddPragmaticDocumentsValueGenerators(); // what the generated host calls, from the assembly's metadata
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        var unitOfWork = new DocUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(),
            generatedValues: scope.ServiceProvider.GetServices<IGeneratedValueBinding>());
        IRepository<SupportCase> cases = new SupportCase.DocRepository(unitOfWork);
        var first = new SupportCase { Subject = "The lift is stuck" };
        cases.Add(first);
        await unitOfWork.SaveChangesAsync();
        var second = new SupportCase { Subject = "No hot water" };
        cases.Add(second);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal("CASE-0001", first.Number); // the database is the test's own: its counter starts there
        Assert.Equal("CASE-0002", second.Number);
        Assert.Matches(new Regex("^CASE-[0-9]{4}$"), second.Number);
    }
}
