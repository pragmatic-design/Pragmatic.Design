using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     Two operations at the same address — the correct definition of «duplicate».
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A check on entity, mode and payload shape does not work: two commands that take only an
///         <c>Id</c> are two different commands. The payload is the row's identity, the verb is the name.
///     </para>
///     <para>
///         What is really observable and really in conflict is the <b>route</b>: two operations at the same
///         address, between which a caller cannot choose. <c>PRAG0529</c> sees it inside a compilation;
///         across different libraries nobody sees it except the host, and that is <c>PRAG1697</c>.
///     </para>
///     <para>
///         ⚠️ <b>This case cannot be a runtime test</b>: the collision is a compile error, so an application
///         that has it does not exist. What is measured here is the other half — that routes that
///         <em>differ only by group</em> are not mistaken for a conflict.
///     </para>
/// </remarks>
public class TheSameAddressTwice(PostgresFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>
    ///     Two modules, two routes that look alike, both reachable.
    /// </summary>
    [Fact]
    public async Task RoutesThatDifferOnlyByModule_BothAnswer()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        var orderId = created.GetProperty("id").GetGuid();

        var sales = await PutAsync($"/api/orders/{orderId}/reference", new
        {
            reference = "ORD-RENAMED",
        });

        sales.IsSuccessStatusCode.Should().BeTrue(
            await sales.Content.ReadAsStringAsync());

        // The catalog operation exists and has an address of its own: if the host's check confused the
        // two modules, this application would not compile at all.
        var catalog = await PutAsync($"/api/catalog-items/{Guid.CreateVersion7()}", new
        {
            listPrice = 1.00m,
        });

        catalog.StatusCode.Should().NotBe(System.Net.HttpStatusCode.InternalServerError,
            "the route is registered and answers: 404 on an id that does not exist, not 500");
    }
}
