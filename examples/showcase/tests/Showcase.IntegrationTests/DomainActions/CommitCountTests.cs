using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Invoker;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.Catalog.Amenities.Actions;
using Showcase.Catalog.Amenities.Mutations;
using Showcase.Catalog.Enums;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.DomainActions;

/// <summary>
///     How many transactions each way of writing several rows actually costs.
/// </summary>
/// <remarks>
///     <para>
///         The question a developer asks is "should this be a mutation or a list of setters?", and the
///         answer is a number, not a preference: a mutation invoked in a loop is one transaction per
///         iteration, while an action that builds the entities itself commits once. Counting
///         <c>SaveChanges</c> in the generated code cannot answer it — every invoker contains exactly
///         one call. What matters is how many times it runs.
///     </para>
///     <para>
///         Measured by subscribing to the boundary <c>DbContext</c>'s <c>SavingChanges</c> in the same
///         scope the invokers resolve theirs from, so the count is the database's, not the test's idea
///         of it.
///     </para>
/// </remarks>
public class CommitCountTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private static string UniquePrefix() => $"CNT{Guid.NewGuid():N}"[..12];

    private static CreateAmenityMutation Amenity(string name) =>
        new() { Name = name, Category = AmenityCategory.Spa };

    /// <summary>
    ///     Runs <paramref name="work" /> in one DI scope and returns how many times that scope's
    ///     Catalog <c>DbContext</c> committed.
    /// </summary>
    private async Task<int> CountCommitsAsync(Func<IServiceProvider, Task> work)
    {
        using var scope = Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));
        var commits = 0;
        db.SavingChanges += (_, _) => commits++;

        // The same internal-call scope the generated CompositeInvoker opens around its steps. Without
        // it a mutation invoked outside a request checks its own [RequirePermission] and returns
        // UNAUTHORIZED — an authorization question, not the transactional one being measured here.
        var callContext = scope.ServiceProvider
            .GetService<Pragmatic.Actions.Pipeline.ActionCallContext>();
        using var internalCall = callContext?.EnterInternalCall();

        await work(scope.ServiceProvider);
        return commits;
    }

    private static Task<Pragmatic.Result.Result<Amenity, Pragmatic.Result.IError>> CreateAsync(
        IServiceProvider sp, string name) =>
        sp.GetRequiredService<IMutationInvoker<CreateAmenityMutation, Amenity>>()
            .InvokeAsync(Amenity(name));

    /// <summary>
    ///     The number that decides the guidance: a mutation is a transaction, so a loop of mutations is
    ///     a loop of transactions.
    /// </summary>
    [Fact]
    public async Task AMutationInvokedThreeTimes_CommitsThreeTimes()
    {
        var prefix = UniquePrefix();

        var commits = await CountCommitsAsync(async sp =>
        {
            for (var i = 0; i < 3; i++)
            {
                var r = await CreateAsync(sp, $"{prefix}-{i}");
                r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.Code : "");
            }
        });

        commits.Should().Be(3,
            "invoked from outside any action each one is its own root, so each one commits — the "
            + "sharing only starts when something outer already holds this unit of work");
    }

    /// <summary>
    ///     The composite is the answer when the steps must be atomic: two mutations, one transaction.
    /// </summary>
    [Fact]
    public async Task ACompositeOfTwoMutations_CommitsOnce()
    {
        var prefix = UniquePrefix();

        var commits = await CountCommitsAsync(async sp =>
        {
            var result = await sp.GetRequiredService<IVoidDomainActionInvoker<CreateAmenityPairAction>>()
                .InvokeAsync(new CreateAmenityPairAction
                {
                    First = Amenity($"{prefix}-A"),
                    Second = Amenity($"{prefix}-B")
                });

            result.IsSuccess.Should().BeTrue();
        });

        commits.Should().Be(1,
            "the steps run without saving and the composite commits once — that is what makes it "
            + "atomic. Measured 2 before CommitsItsOwnWork existed: the outer pipeline committed again "
            + "over nothing, costing a round trip and making the documented guarantee false");
    }

    /// <summary>
    ///     A batch naming the unit of work it covers defers that one, and the opener saves it.
    /// </summary>
    /// <remarks>
    ///     The shape this replaced: <c>new BatchContext()</c> with no unit of work suppressed every
    ///     commit and performed none, so three mutations reported success and wrote nothing. Naming the
    ///     unit of work keeps the suppression inside a boundary the opener can actually save.
    /// </remarks>
    [Fact]
    public async Task ABatchNamingItsUnitOfWork_DefersUntilTheOpenerSaves()
    {
        var prefix = UniquePrefix();

        var commits = await CountCommitsAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredKeyedService<IUnitOfWork>(typeof(CatalogBoundary));

            using (new BatchContext(unitOfWork))
            {
                for (var i = 0; i < 3; i++)
                    (await CreateAsync(sp, $"{prefix}-{i}")).IsSuccess.Should().BeTrue();
            }

            await unitOfWork.SaveChangesAsync();
        });

        commits.Should().Be(1,
            "the three mutations defer into the batch and the opener performs the single save");
    }

    /// <summary>
    ///     A batch does not reach across boundaries: the one it does not name commits itself.
    /// </summary>
    /// <remarks>
    ///     This is the shape of every cross-boundary call, and the reason the batch had to carry a unit
    ///     of work. While it was ambient and global it suppressed the callee's commit too — and the
    ///     opener could only ever save its own, so that row was staged, reported as written, and lost.
    /// </remarks>
    [Fact]
    public async Task ABatchDoesNotReachIntoAnotherBoundary()
    {
        using var scope = Services.CreateScope();

        var catalog = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));
        var billing = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(Showcase.Billing.BillingBoundary));

        var catalogCommits = 0;
        var billingCommits = 0;
        catalog.SavingChanges += (_, _) => catalogCommits++;
        billing.SavingChanges += (_, _) => billingCommits++;

        var callContext = scope.ServiceProvider.GetService<Pragmatic.Actions.Pipeline.ActionCallContext>();
        using var internalCall = callContext?.EnterInternalCall();

        var prefix = UniquePrefix();

        var catalogUnitOfWork = scope.ServiceProvider
            .GetRequiredKeyedService<IUnitOfWork>(typeof(CatalogBoundary));

        using (new BatchContext(catalogUnitOfWork))
        {
            (await CreateAsync(scope.ServiceProvider, $"{prefix}-A")).IsSuccess.Should().BeTrue();

            var invoice = await scope.ServiceProvider
                .GetRequiredService<IMutationInvoker<Showcase.Billing.Mutations.CreateDraftInvoiceMutation,
                    Showcase.Billing.Entities.Invoice>>()
                .InvokeAsync(new Showcase.Billing.Mutations.CreateDraftInvoiceMutation
                {
                    ReservationId = Guid.NewGuid(),
                    GuestId = Guid.NewGuid(),
                    SubTotal = 100m,
                    TaxAmount = 10m,
                    TotalAmount = 110m,
                    Currency = "EUR",
                    IssuedAt = DateTimeOffset.UtcNow
                });

            invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.Code : "");
        }

        catalogCommits.Should().Be(0,
            "the batch names Catalog's unit of work, so Catalog defers — and this test deliberately "
            + "never saves it, which is the opener's choice and their responsibility");
        billingCommits.Should().Be(1,
            "Billing is not the unit of work this batch names, so it commits itself. Before the batch "
            + "carried one it was suppressed here and nobody could save it: the row was lost");
    }
}
