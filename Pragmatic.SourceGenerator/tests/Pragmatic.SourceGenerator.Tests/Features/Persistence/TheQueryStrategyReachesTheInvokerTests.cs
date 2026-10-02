using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[QueryStrategy]</c> reaches the source the read starts from.
/// </summary>
/// <remarks>
///     <para>
///         The attribute is public and documented; if no transform read it, every strategy would produce
///         the same tracked, filtered <c>Set&lt;TEntity&gt;()</c>. A declaration that changes nothing is
///         worse than a missing one: the author reads the code and believes the read is untracked.
///     </para>
///     <para>
///         ⚠️ The source is built in the query's invoker, not in the endpoint handler, so these cases
///         live here rather than in <c>Pragmatic.Endpoints.Tests</c>: that suite does not reference EF
///         Core, so no invoker is generated in it. The fact still matters: a strategy that stopped
///         reaching the source would drop soft delete and tenant isolation, or start tracking rows a
///         projection said were read-only.
///     </para>
///     <para>
///         The absence cases are here too. Without them a template that emitted <c>AsNoTracking</c>
///         unconditionally would pass every positive assertion.
///     </para>
/// </remarks>
public class TheQueryStrategyReachesTheInvokerTests
{
    private const string Preamble = """
        using System;
        using System.Linq;
        using System.Linq.Expressions;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp.Queries;

        public sealed class LedgerBoundary { }

        [Entity]
        [BelongsTo<LedgerBoundary>]
        public partial class Ledger : IEntity
        {
            public Guid Id { get; set; }
            public Guid PersistenceId { get => Id; set => Id = value; }
            public string Name { get; set; } = "";
        }

        public class LedgerDto
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";

            public static Expression<Func<Ledger, LedgerDto>> Projection =>
                l => new LedgerDto { Id = l.Id, Name = l.Name };
        }

        [PragmaticDbContext("Ledgers")]
        public partial class LedgerDbContext { }
        """;

    private static string InvokerFor(string declaration, string queryTypeName)
    {
        var (sources, _) = TraitCompilationHarness.Generate(Preamble + "\n" + declaration);

        var invoker = sources
            .Where(pair => pair.Key.Contains(queryTypeName) && pair.Key.EndsWith(".QueryInvoker.g.cs"))
            .Select(pair => pair.Value)
            .FirstOrDefault();

        invoker.Should().NotBeNull("the query is declared, so it has an invoker");
        return invoker!;
    }

    [Fact]
    public void Query_WithRawStrategy_ReadsUntrackedAndWithoutFilters()
    {
        var invoker = InvokerFor("""
            [QueryStrategy(Strategy = QueryStrategy.Raw)]
            [Query<Ledger, LedgerDto>]
            [Endpoint(HttpVerb.Get, "/ledgers/raw")]
            public partial class RawLedgersQuery
            {
                public int Page { get; set; } = 1;
                public int PageSize { get; set; } = 20;
            }
            """, "RawLedgersQuery");

        invoker.Should().Contain("AsNoTracking", "Raw is \"no tracking\" by its own definition");
        invoker.Should().Contain("IgnoreQueryFilters", "Raw is \"no filters\" by its own definition");
    }

    [Fact]
    public void Query_WithProjectionStrategy_ReadsUntrackedAndKeepsTheFilters()
    {
        var invoker = InvokerFor("""
            [QueryStrategy(Strategy = QueryStrategy.Projection)]
            [Query<Ledger, LedgerDto>]
            [Endpoint(HttpVerb.Get, "/ledgers/projected")]
            public partial class ProjectedLedgersQuery
            {
                public int Page { get; set; } = 1;
                public int PageSize { get; set; } = 20;
            }
            """, "ProjectedLedgersQuery");

        invoker.Should().Contain("AsNoTracking", "a projection is never written back");
        invoker.Should().NotContain("IgnoreQueryFilters",
            "Projection says nothing about filters — dropping them here would make soft delete and "
            + "tenant isolation depend on how the DTO is built");
    }

    [Fact]
    public void Query_WithEntityStrategy_ReadsTrackedAndFiltered()
    {
        var invoker = InvokerFor("""
            [QueryStrategy(Strategy = QueryStrategy.Entity)]
            [Query<Ledger, LedgerDto>]
            [Endpoint(HttpVerb.Get, "/ledgers/entity")]
            public partial class EntityLedgersQuery
            {
                public int Page { get; set; } = 1;
                public int PageSize { get; set; } = 20;
            }
            """, "EntityLedgersQuery");

        invoker.Should().NotContain("AsNoTracking");
        invoker.Should().NotContain("IgnoreQueryFilters");
    }

    [Fact]
    public void Query_WithoutStrategy_ReadsTrackedAndFiltered()
    {
        var invoker = InvokerFor("""
            [Query<Ledger, LedgerDto>]
            [Endpoint(HttpVerb.Get, "/ledgers")]
            public partial class ListLedgersQuery
            {
                public int Page { get; set; } = 1;
                public int PageSize { get; set; } = 20;
            }
            """, "ListLedgersQuery");

        invoker.Should().NotContain("AsNoTracking",
            "the default is unchanged: a query that declares nothing reads the way it always did");
        invoker.Should().NotContain("IgnoreQueryFilters");
    }
}
