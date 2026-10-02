using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Query.Builder;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     The two hints of the fluent <c>QueryBuilder</c> do what they say.
/// </summary>
/// <remarks>
///     <para>
///         <c>AsNoTracking()</c> and <c>AsSplitQuery()</c> are EF Core concepts, and
///         <c>Pragmatic.Persistence</c> does not reference EF Core. A field set and read by nobody would give
///         the caller the opposite of what they asked for, without a warning.
///     </para>
///     <para>
///         ⚠️ «Declared and never read» says the implementation is missing, not that the capability is not
///         needed. <c>IQueryHintApplier</c> is declared where there is no EF, and installed by
///         <c>AddPragmaticPersistenceEFCore</c> where there is — the same shape as <c>INavigationLoader</c>.
///     </para>
///     <para>
///         ⚠️ The <b>generated</b> path is a separate one: a <c>[Query]</c> carries <c>IQueryHints</c> and
///         <c>EfCoreQueryExecutor</c> applies them. This case is about the fluent surface.
///     </para>
/// </remarks>
public class TheQueryBuilderHints(PostgresFixture fixture) : E2ETestBase(fixture)
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

    /// <summary>
    ///     The hint asked of the <b>builder</b> reaches EF.
    /// </summary>
    [Fact]
    public async Task AsNoTrackingOnTheBuilder_IsHonoured()
    {
        var id = await AnOrderAsync();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        db.ChangeTracker.Clear();

        var built = new QueryBuilder<Order>()
            .AsNoTracking()
            .WithFilter(o => o.PersistenceId == id)
            .Build(db.Set<Order>());

        var order = await built.SingleAsync();

        db.Entry(order).State.Should().Be(EntityState.Detached,
            "«Detached» means not tracked: the builder's hint reached EF");
    }

    /// <summary>
    ///     ⚠️ The contrast that isolates the variable: without the hint, the same query tracks.
    /// </summary>
    /// <remarks>
    ///     Without it, the case above would prove only that something does not track — and it would also pass
    ///     on a context that never tracks, exactly the scenario in which an unimplemented hint looks like it
    ///     works.
    /// </remarks>
    [Fact]
    public async Task WithoutIt_TheSameQueryTracks()
    {
        var id = await AnOrderAsync();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        db.ChangeTracker.Clear();

        var built = new QueryBuilder<Order>()
            .WithFilter(o => o.PersistenceId == id)
            .Build(db.Set<Order>());

        var order = await built.SingleAsync();

        db.Entry(order).State.Should().Be(EntityState.Unchanged,
            "the difference is the hint, not the context");
    }
}
