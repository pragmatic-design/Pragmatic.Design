using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Pragmatic;
using Pragmatic.Specification;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Specifications written as Pragmatic's are: named factories over Spec&lt;T&gt;.Where, with parameters, composed with &amp; | ! and
///     AndIf, in the partial class Persistence generates for the entity (its <c>ById</c> beside them).
/// </summary>
public static partial class ReviewSpecifications
{
    public static Specification<Review> Published() => Spec<Review>.Where(r => r.Published);

    public static Specification<Review> By(string owner) => Spec<Review>.Where(r => r.OwnerId == owner);

    public static Specification<Review> StartingWith(string prefix) => Spec<Review>.Where(r => r.Text.StartsWith(prefix));

    public static Specification<Review> At(Guid[] properties) => Spec<Review>.Where(r => properties.Contains(r.PropertyId));

    public static Specification<Review> Search(string? owner, bool onlyPublished)
        => Spec<Review>.True.AndIf(owner is not null, () => By(owner!)).AndIf(onlyPublished, Published());
}

/// <summary>
///     Specifications on the Sql engine: compiled where they are written, composed at runtime (<c>Matching</c>), on the five databases. The
///     oracle is the same specification run in .NET (<c>ToExpression().Compile()</c>) over every row, read in Raw mode.
/// </summary>
[Collection("pragmatic databases")]
public class SpecificationTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<(SqlSession Session, List<Review> All)> Seeded(SqlDialect dialect)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
            return (session, await session.Query<Review>().ToListAsync());
    }

    private static async Task<List<string>> Read(SqlSession session, ISpecification<Review> specification)
        => await session.Query<Review>().Matching(SqlSpecifications.ToPredicate(specification)).OrderBy(r => r.Text).Select(r => r.Text).ToListAsync();

    private static List<string> Expected(List<Review> rows, Specification<Review> specification)
        => rows.Where(specification.ToExpression().Compile()).Select(r => r.Text).Order(StringComparer.Ordinal).ToList();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_composed_specification_reads_what_it_selects_in_memory(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, all) = await Seeded(dialect);
        using (session)
        using (_toggle.UseMode(FilterMode.Raw)) // the specification alone: no family in the way
        {
            var specifications = new Specification<Review>[]
            {
                ReviewSpecifications.Published(),
                ReviewSpecifications.Published() & ReviewSpecifications.By("anna"),
                ReviewSpecifications.By("bruno") | !ReviewSpecifications.Published(),
                !(ReviewSpecifications.StartingWith("anna@") & ReviewSpecifications.Published()),
                ReviewSpecifications.Search("anna", onlyPublished: true),
                ReviewSpecifications.Search(null, onlyPublished: false), // Spec.True: every row
                ReviewSpecifications.At(all.Where(r => r.Text.EndsWith("RIVA", StringComparison.Ordinal)).Select(r => r.PropertyId).Distinct().ToArray()),
                ReviewSpecifications.At([]), // an empty IN list matches nothing
            };
            foreach (var specification in specifications)
                Assert.Equal(Expected(all, specification), await Read(session, specification));
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_specification_reads_inside_the_callers_filters(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await Seeded(dialect);
        using (session)
        {
            // anna's published reviews (the families and the rule), then the specification on top: the StartsWith narrows to RIVA's.
            Assert.Equal(["anna@MARE", "anna@RIVA"], await Read(session, ReviewSpecifications.By("bruno") | ReviewSpecifications.StartingWith("anna@")));
            Assert.Equal(["anna@RIVA"], await Read(session, ReviewSpecifications.StartingWith("anna@R")));
        }
    }

    [Fact]
    public void A_specification_the_engine_did_not_compile_is_refused()
    {
        var foreign = new Uncompiled();
        var refused = Assert.Throws<NotSupportedException>(() => SqlSpecifications.ToPredicate(ReviewSpecifications.Published() & foreign));
        Assert.Contains("was not compiled by the Sql engine", refused.Message, StringComparison.Ordinal);
    }

    private sealed class Uncompiled : Specification<Review>
    {
        public override System.Linq.Expressions.Expression<Func<Review, bool>> ToExpression() => r => r.Text.Length > 3;
    }
}
