using System.Net;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>[LoadFrom&lt;TQuery&gt;]</c> on a real database: an operation's data read by a declared query, through
///     the query's own invoker — its read, its 404, its permission.
/// </summary>
/// <remarks>
///     Not through the boundary's internal facade: that enters an internal call, and the query's
///     permission would not be asked of the caller at all.
/// </remarks>
public class TheDataAQueryAnswers(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<(Guid Id, string Reference)> AnOrderAsync()
    {
        var reference = $"ORD-{Guid.NewGuid():N}"[..14];
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference,
            lines = Array.Empty<object>(),
        }));

        return (created.GetProperty("id").GetGuid(), reference);
    }

    [Fact]
    public async Task TheOperation_ReadsWhatTheQueryAnswers()
    {
        var (id, reference) = await AnOrderAsync();

        var read = await ReadAsync(await PostAsync("/api/order-reads/reference", new { id }));

        read.GetString().Should().Be(reference);
    }

    [Fact]
    public async Task ASingleQueryThatFindsNothing_FailsTheOperationWith404()
    {
        var response = await PostAsync("/api/order-reads/reference", new { id = Guid.NewGuid() });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
    }

    /// <summary>The control: a caller holding the query's permission gets its answer.</summary>
    [Fact]
    public async Task ACallerWithTheQuerysPermission_GetsTheAnswer()
    {
        var (_, reference) = await AnOrderAsync();
        SignedIn("conformance.order.guardedread");

        var count = await ReadAsync(await PostAsync("/api/order-reads/guarded-count", new { reference }));

        count.GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ACallerWithoutIt_IsForbidden_ThroughTheOperation()
    {
        var (_, reference) = await AnOrderAsync();
        SignedIn("sales.label.read");

        var response = await PostAsync("/api/order-reads/guarded-count", new { reference });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
    }

    private void SignedIn(string permission)
    {
        Client.DefaultRequestHeaders.Add("X-User-Id", "order-reader");
        Client.DefaultRequestHeaders.Add("X-User-Name", "order-reader");
        Client.DefaultRequestHeaders.Add("X-User-Permissions", permission);
    }
}
