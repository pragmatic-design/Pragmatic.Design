using System.Net;
using Conformance.Split;
using Conformance.Split.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>[Owns&lt;T&gt;]</c>: in an assembly with two boundaries, the table sits in the context of whoever
///     claimed it, and the operation writing it goes through that context.
/// </summary>
/// <remarks>
///     <para>
///         The declaration is needed <b>only</b> here: with a single boundary it owns everything the assembly
///         declares, and there is nothing to choose. Every other example in the repository has that shape,
///         and that is why <c>Conformance.Split</c> exists — two boundaries, each claiming one of the two
///         entities.
///     </para>
///     <para>
///         ⚠️ The two halves of the case are two different things. The first is whose <b>table</b> it is,
///         read on the model of the two <c>DbContext</c>s. The second is that the operation <b>runs</b>: the
///         mutation names no boundary, the generator derives it from the entity it writes, and without that
///         step the route would find no unit of work. The two can fail for different reasons — no keyed
///         registration, or no mapped route (404).
///     </para>
///     <para>
///         Each half carries its own control: it also asserts that the other boundary's context does
///         <b>not</b> know the entity. Without it, «it sits in the right context» would be satisfied by a
///         generator that puts every entity in every context.
///     </para>
/// </remarks>
public class TheTableInItsOwnersContext(PostgresFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public void EachBoundaryContext_CarriesOnlyItsOwnEntity()
    {
        using var scope = Services.CreateScope();

        var ledger = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(LedgerBoundary));
        var journal = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(JournalBoundary));

        ledger.Model.FindEntityType(typeof(Ledger)).Should().NotBeNull(
            "[Owns<Ledger>] sits on LedgerBoundary");
        ledger.Model.FindEntityType(typeof(Journal)).Should().BeNull(
            "and Journal belongs to the other one, in the same assembly");

        journal.Model.FindEntityType(typeof(Journal)).Should().NotBeNull();
        journal.Model.FindEntityType(typeof(Ledger)).Should().BeNull();
    }

    [Fact]
    public async Task EachRoute_WritesThroughItsOwnBoundary()
    {
        var ledger = await PostAsync("/api/ledgers", new { name = $"L-{Guid.NewGuid():N}"[..10] });
        ledger.StatusCode.Should().Be(HttpStatusCode.Created, await ledger.Content.ReadAsStringAsync());

        var journal = await PostAsync("/api/journals", new { name = $"J-{Guid.NewGuid():N}"[..10] });
        journal.StatusCode.Should().Be(HttpStatusCode.Created, await journal.Content.ReadAsStringAsync());
    }
}
