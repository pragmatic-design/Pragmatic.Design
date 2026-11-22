using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     A paged query is read in an order: the key when it declares no sort, its own sort when it does (#938).
/// </summary>
/// <remarks>
///     The shape, beside the effect: <c>APagedQueryWithoutASort</c> in the conformance suite reads the
///     <c>ORDER BY</c> on the SQL a real host sends. These say which order, and that the declared one wins.
/// </remarks>
public class APagedQueryIsReadInAnOrderTests
{
    private const string Entity = """
        using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public string Code { get; private set; } = "";
        }
        """;

    private const string KeyOrder = "query = query.OrderBy(e => e.PersistenceId);";

    [Fact]
    public void APagedQueryWithNoSort_IsOrderedByTheMappedKey_AndSaysSo()
    {
        var result = Run(Entity + """

            [Query<Order>(Paged = true)]
            public partial class ListOrdersQuery;
            """);

        Apply(result).Should().Contain(KeyOrder,
            "the generated Id is an unmapped alias of PersistenceId, so the key EF Core can order by is PersistenceId");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0746").Should().BeTrue(
            "the order is said where the query is written, or it is visible only in the SQL");
    }

    [Fact]
    public void APagedQueryWithASortDefault_FallsBackToTheKeyOnlyWhenNoSortApplied()
    {
        var result = Run(Entity + """

            [Query<Order>(Paged = true)]
            public partial class ListOrdersQuery
            {
                [Sort(DefaultDirection = SortDirection.Descending)]
                public SortDirection? CodeSort { get; init; }
            }
            """);

        var apply = Apply(result);
        var declared = apply.IndexOf("query = query.OrderByDescending(e => e.Code);", StringComparison.Ordinal);
        var fallback = apply.IndexOf(KeyOrder, StringComparison.Ordinal);

        declared.Should().BeGreaterThan(-1, "the declared default orders the page");
        fallback.Should().BeGreaterThan(declared,
            "the key comes after the declared sorts, under isFirstSort, so it orders the page only when none did; "
            + "a default always does, which keeps the declared order first");
        apply.Substring(declared, fallback - declared).Should().Contain("if (isFirstSort)");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0746").Should().BeFalse(
            "a query that declares its order is not told about the fallback");
    }

    /// <summary>The control: a query that is not paged gets no order it did not ask for.</summary>
    [Fact]
    public void AQueryThatIsNotPaged_GetsNoOrder()
    {
        var result = Run(Entity + """

            [Query<Order>]
            public partial class ListOrdersQuery;
            """);

        Apply(result).Should().NotContain("OrderBy(");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0746").Should().BeFalse();
    }

    private static string Apply(SourceGenRunResult result)
    {
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "ListOrdersQuery.Query");
        generated.Should().NotBeNull();
        return generated!;
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References());

    private static MetadataReference[] References() =>
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BelongsToAttribute<object>>(),
        GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
        GeneratorTestHelper.FromType<Specification.Specification<object>>(),
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
    ];
}
