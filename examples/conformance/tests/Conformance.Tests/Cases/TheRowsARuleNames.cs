using System.Net;
using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>[LoadEntity(Specification = …)]</c> and <c>[LoadEntities(Specification = …)]</c> on a real database:
///     the rows a named rule matches, its parameters bound by name to the operation's properties.
/// </summary>
/// <remarks>
///     Each test names its labels with a prefix of its own: the database is shared by the
///     collection, and a rule on names would otherwise match another test's rows.
/// </remarks>
public class TheRowsARuleNames(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private static string APrefix() => $"r{Guid.NewGuid():N}"[..12];

    private async Task<Guid> ALabelAsync(string name)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        // The name has a private setter and is no parameter of the factory: written through the entry,
        // as TheLinkedRows does.
        var label = Label.Create();
        db.Add(label);
        db.Entry(label).Property(nameof(Label.Name)).CurrentValue = name;
        await db.SaveChangesAsync();

        return label.PersistenceId;
    }

    [Fact]
    public async Task ARow_IsReadByTheRule()
    {
        var name = APrefix() + "-urgent";
        var id = await ALabelAsync(name);

        var found = await ReadAsync(await PostAsync("/api/labels/find", new { name }));

        found.GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task ARuleThatMatchesNothing_Is404()
    {
        var response = await PostAsync("/api/labels/find", new { name = APrefix() + "-nowhere" });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
    }

    [Fact]
    public async Task Rows_AreReadByTheRule()
    {
        var prefix = APrefix();
        await ALabelAsync(prefix + "-a");
        await ALabelAsync(prefix + "-b");
        await ALabelAsync(APrefix() + "-elsewhere");

        var names = await ReadAsync(await PostAsync("/api/labels/starting-with", new { prefix }));

        names.EnumerateArray().Select(n => n.GetString()).Order(StringComparer.Ordinal)
            .Should().Equal(prefix + "-a", prefix + "-b");
    }

    /// <summary>RequireAny: the operation needs at least one row, so none is a 404 — not an empty list.</summary>
    [Fact]
    public async Task NoRow_WithRequireAny_Is404()
    {
        var response = await PostAsync("/api/labels/starting-with", new { prefix = APrefix() });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
    }

    [Fact]
    public async Task ACallerWhoMayReadLabels_GetsThem()
    {
        var prefix = APrefix();
        await ALabelAsync(prefix + "-a");
        SignedIn("sales.label.read");

        var names = await ReadAsync(await PostAsync("/api/labels/starting-with/for-a-reader", new { prefix }));

        names.EnumerateArray().Select(n => n.GetString()).Should().Equal(prefix + "-a");
    }

    [Fact]
    public async Task ACallerWhoMayNot_IsForbidden_BeforeTheRowsAreRead()
    {
        var prefix = APrefix();
        await ALabelAsync(prefix + "-a");
        SignedIn("sales.order.read");

        var response = await PostAsync("/api/labels/starting-with/for-a-reader", new { prefix });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        body.Should().NotContain(prefix, "a refused caller learns nothing of the rows");
    }

    [Fact]
    public async Task NobodySignedIn_IsUnauthorized()
    {
        var response = await PostAsync("/api/labels/starting-with/for-a-reader", new { prefix = APrefix() });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, body);
    }

    private void SignedIn(string permission)
    {
        Client.DefaultRequestHeaders.Add("X-User-Id", "label-reader");
        Client.DefaultRequestHeaders.Add("X-User-Name", "label-reader");
        Client.DefaultRequestHeaders.Add("X-User-Permissions", permission);
    }
}
