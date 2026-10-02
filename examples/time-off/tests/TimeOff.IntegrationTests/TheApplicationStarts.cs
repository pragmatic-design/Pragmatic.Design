using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;

namespace TimeOff.IntegrationTests;

/// <summary>
///     The application starts on an empty database and publishes its contract.
/// </summary>
public sealed class TheApplicationStarts(PostgresFixture database) : TimeOffTestBase(database)
{
    [Fact]
    public async Task OnAnEmptyDatabase_ItStarts_AndServesItsOpenApiDocument()
    {
        var document = await ReadJsonAsync(await Client.GetAsync("/openapi/v1.json"));

        document.GetProperty("openapi").GetString().Should().StartWith("3.");
    }
}
