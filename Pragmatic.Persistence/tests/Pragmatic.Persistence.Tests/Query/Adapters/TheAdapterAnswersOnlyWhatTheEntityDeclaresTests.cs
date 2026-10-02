using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Testing.Assertions;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     A grid adapter answers on the fields the entity <b>declared</b>, and on nothing else.
/// </summary>
/// <remarks>
///     <para>
///         The client supplies the field name. The generated bridge is an allowlist — it names only the
///         properties carrying <c>[Filterable]</c> — and the adapters were not: they resolved any public
///         scalar and refused a fixed list of sensitive names. Two surfaces for one job, and the adapter
///         was the wider half.
///     </para>
///     <para>
///         ⚠️ Why a wider surface is not a small thing: sorting or filtering on a column makes it talk
///         without reading it. <c>sortField=Margin</c> orders the page by a number nobody published, and
///         <c>equals</c> answers whether a row holds a given value — a boolean oracle over a column the
///         entity never offered. A denylist covers what somebody remembered to write down; this is the
///         same shape as an allowlist that nobody consults.
///     </para>
///     <para>
///         The entity declares its fields through <c>GridFieldRegistry</c>, which the generator fills
///         from the same list the bridge switches on. Where an entity declares nothing the old guard
///         still applies — that residue is stated on the policy itself.
///     </para>
/// </remarks>
public class TheAdapterAnswersOnlyWhatTheEntityDeclaresTests
{
    /// <summary>An entity that publishes two fields and holds four.</summary>
    private sealed class Deal
    {
        public Guid Id { get; set; }
        public string Reference { get; set; } = "";
        public int Stage { get; set; }

        /// <summary>Declared by nobody: it is the column the client must not be able to interrogate.</summary>
        public decimal Margin { get; set; }
    }

    public TheAdapterAnswersOnlyWhatTheEntityDeclaresTests()
        => GridFieldRegistry.Declare<Deal>([nameof(Deal.Reference), nameof(Deal.Stage)]);

    private static IQueryable<Deal> Deals() =>
    new[]
    {
        new Deal { Id = Guid.NewGuid(), Reference = "D-001", Stage = 1, Margin = 10m },
        new Deal { Id = Guid.NewGuid(), Reference = "D-002", Stage = 2, Margin = 90m },
    }.AsQueryable();

    /// <summary>A filter on a field the entity did not declare changes nothing.</summary>
    [Fact]
    public void FilteringOnAnUndeclaredField_IsRefused()
    {
        var filtered = PersistenceQuery.For<Deal>()
            .FromPrimeNG(new PrimeNGLazyLoadEvent
            {
                Filters = new Dictionary<string, PrimeNGFilterMetadata>
                {
                    ["Margin"] = new() { Value = 90m, MatchMode = "equals" }
                }
            })
            .Build(Deals())
            .ToList();

        filtered.Should().HaveCount(2,
            "an undeclared field is not a filter: answering one row would tell the caller which row holds 90");
    }

    /// <summary>And neither does a sort on one.</summary>
    /// <remarks>
    ///     Ordering is the quieter half of the same leak: it never returns the value, and it ranks the
    ///     page by it.
    /// </remarks>
    [Fact]
    public void SortingOnAnUndeclaredField_IsRefused()
    {
        var sorted = PersistenceQuery.For<Deal>()
            .FromPrimeNG(new PrimeNGLazyLoadEvent { SortField = "Margin", SortOrder = -1 })
            .Build(Deals())
            .ToList();

        sorted.Select(d => d.Reference).Should().Equal(["D-001", "D-002"],
            "the rows keep the order they had: the sort was refused, not applied backwards");
    }

    /// <summary>The control: a declared field still filters, or the guard is just an off switch.</summary>
    [Fact]
    public void ADeclaredField_StillFilters()
    {
        var filtered = PersistenceQuery.For<Deal>()
            .FromPrimeNG(new PrimeNGLazyLoadEvent
            {
                Filters = new Dictionary<string, PrimeNGFilterMetadata>
                {
                    ["Stage"] = new() { Value = 2, MatchMode = "equals" }
                }
            })
            .Build(Deals())
            .ToList();

        filtered.Should().ContainSingle().Which.Reference.Should().Be("D-002");
    }

    /// <summary>The second control: a declared field still sorts.</summary>
    [Fact]
    public void ADeclaredField_StillSorts()
    {
        var sorted = PersistenceQuery.For<Deal>()
            .FromPrimeNG(new PrimeNGLazyLoadEvent { SortField = "Stage", SortOrder = -1 })
            .Build(Deals())
            .ToList();

        sorted.Select(d => d.Reference).Should().Equal(["D-002", "D-001"]);
    }

    /// <summary>
    ///     And a sensitive name is refused whether or not the entity declared anything — the two guards
    ///     are independent.
    /// </summary>
    [Fact]
    public void ASensitiveName_IsStillRefused()
    {
        var filtered = PersistenceQuery.For<Deal>()
            .FromPrimeNG(new PrimeNGLazyLoadEvent
            {
                Filters = new Dictionary<string, PrimeNGFilterMetadata>
                {
                    ["Id"] = new() { Value = Guid.NewGuid(), MatchMode = "equals" }
                }
            })
            .Build(Deals())
            .ToList();

        filtered.Should().HaveCount(2, "Id is not among the declared fields either");
    }
}
