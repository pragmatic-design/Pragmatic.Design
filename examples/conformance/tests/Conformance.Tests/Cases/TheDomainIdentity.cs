using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     An entity's two identities, and the difference paid at runtime.
/// </summary>
/// <remarks>
///     <para>
///         The generator emits <c>PersistenceId</c>, which is the column, and <c>Id</c>, which is the domain
///         identity. These cases measure the consequence of the two roles.
///     </para>
///     <para>
///         ⚠️ <c>Id</c> is emitted as <c>public Guid Id =&gt; PersistenceId;</c> — an expression, not a
///         column. EF does not map it, so <b>it cannot be translated to SQL</b>: a
///         <c>Where(e =&gt; e.Id == x)</c> compiles and then throws at runtime. Queries must name
///         <c>PersistenceId</c>, and that is what the generated code does.
///     </para>
/// </remarks>
public class TheDomainIdentity(PostgresFixture fixture) : E2ETestBase(fixture)
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

    /// <summary><c>Id</c> goes out on the wire, and it equals <c>PersistenceId</c>.</summary>
    [Fact]
    public async Task TheTwoIdentities_AreTheSameValue()
    {
        var id = await AnOrderAsync();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var order = await db.Set<Order>().AsNoTracking().SingleAsync(o => o.PersistenceId == id);

        order.Id.Should().Be(order.PersistenceId,
            "the domain identity is an alias of the row's key, not a second value");
        order.Id.Should().Be(id, "and it is the one the API publishes as `id`");
    }

    /// <summary>
    ///     ⚠️ The cost of the distinction: filtering by <c>Id</c> does not reach the database.
    /// </summary>
    /// <remarks>
    ///     It compiles — it is a public property of the right type — and fails only when executed. That is
    ///     why every generated query names <c>PersistenceId</c>, and why this case exists: the trap is not
    ///     visible by reading the entity.
    /// </remarks>
    [Fact]
    public async Task FilteringByTheDomainIdentity_DoesNotReachTheDatabase()
    {
        var id = await AnOrderAsync();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var byDomainIdentity = async () =>
            await db.Set<Order>().AsNoTracking().SingleAsync(o => o.Id == id);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(byDomainIdentity);

        thrown.Message.Should().Contain("could not be translated",
            "`Id` is a computed expression: EF has no column to translate it from");

        var byColumn = await db.Set<Order>().AsNoTracking().SingleAsync(o => o.PersistenceId == id);
        byColumn.Id.Should().Be(id, "while the column translates, and finds the same row");
    }
}
