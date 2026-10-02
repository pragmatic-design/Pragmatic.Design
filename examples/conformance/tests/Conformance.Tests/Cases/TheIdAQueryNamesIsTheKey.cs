using System.Net;
using Conformance.Tests.Infrastructure;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A query that filters on the entity's <c>Id</c> filters on its key — the same line that works
///     in an update mutation, with no <c>MapTo</c>.
/// </summary>
/// <remarks>
///     The entity's <c>Id</c> is a generated, unmapped alias of <c>PersistenceId</c>. The mutation,
///     the repository, the specification and the resource read all translated it themselves; a
///     hand-written query was the one place the author had to, and forgetting it meant a <c>Where</c>
///     on a property EF Core cannot translate.
/// </remarks>
public class TheIdAQueryNamesIsTheKey(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnOrderAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Two rows, ask for the second: a filter that is dropped answers with the first.</summary>
    [Fact]
    public async Task ASingleQueryOnId_ReturnsTheRowAskedFor()
    {
        await AnOrderAsync();
        var second = await AnOrderAsync();

        var response = await Client.GetAsync($"/api/orders/{second}/as-declared");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(second, (await ReadAsync(response)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ASingleQueryOnId_AnswersNotFoundForAnUnknownId()
    {
        var response = await Client.GetAsync($"/api/orders/{Guid.NewGuid()}/as-declared");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AListQueryOnId_ReturnsExactlyTheRowsAskedFor()
    {
        var first = await AnOrderAsync();
        await AnOrderAsync();
        var third = await AnOrderAsync();

        var response = await Client.GetAsync($"/api/orders/by-id?id={first}&id={third}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        var rows = body.ValueKind == System.Text.Json.JsonValueKind.Array ? body : body.GetProperty("items");
        var ids = rows.EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { first, third }, ids);
    }

    /// <summary>The explicit form keeps working: an author's <c>MapTo</c> still wins.</summary>
    [Fact]
    public async Task TheExplicitMapTo_StillReturnsTheRowAskedFor()
    {
        var order = await AnOrderAsync();

        var response = await Client.GetAsync($"/api/orders/{order}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(order, (await ReadAsync(response)).GetProperty("id").GetGuid());
    }
}
