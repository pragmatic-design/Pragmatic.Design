using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     <c>FilterOps.Range</c> on a <c>[GridFilter&lt;T&gt;]</c> is a bound named by the property: <c>Min…</c>/<c>From…</c>
///     filters <c>&gt;=</c>, <c>Max…</c>/<c>To…</c> filters <c>&lt;=</c> (#330).
/// </summary>
/// <remarks>
///     The generator read no <c>Range</c> at all, so a bound went through the fixed operator and filtered with equality:
///     Showcase's <c>MinStarRating = 3</c> selected the three-star hotels, and a minimum and a maximum together selected
///     nothing unless they were equal. The generated <c>Apply</c> and <c>ToSpecification</c> are run over rows, on a
///     filter deserialized from JSON.
/// </remarks>
public class AGridFilterRangeIsABoundTests
{
    private const string Entity = """
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public class Order
        {
            public string Code { get; set; } = "";
            public decimal Amount { get; set; }
        }
        """;

    private const string Grid = """

        [GridFilter<Order>]
        public partial class OrderGrid
        {
            [Filterable(Operators = FilterOps.Range, MapTo = "Amount")]
            public decimal? MinAmount { get; set; }

            [Filterable(Operators = FilterOps.Range, MapTo = "Amount")]
            public decimal? MaxAmount { get; set; }

            [Filterable(Operators = FilterOps.Range, MapTo = "Amount")]
            public decimal? FromAmount { get; set; }

            [Filterable(Operators = FilterOps.Range, MapTo = "Amount")]
            public decimal? ToAmount { get; set; }
        }
        """;

    private static readonly (string Code, decimal Amount)[] Rows = [("A", 10m), ("B", 20m), ("C", 30m)];

    private static readonly Lazy<Assembly> Compiled = new(() => Compile(Entity + Grid));

    [Theory]
    [InlineData("""{"minAmount":15}""", "B,C")]
    [InlineData("""{"maxAmount":25}""", "A,B")]
    [InlineData("""{"minAmount":15,"maxAmount":25}""", "B")]
    [InlineData("""{"fromAmount":20}""", "B,C")]
    [InlineData("""{"toAmount":20}""", "A,B")]
    [InlineData("""{"minAmount":20,"maxAmount":20}""", "B")]
    public void ABound_KeepsTheRowsOnItsSide(string filter, string expected)
    {
        Codes(filter, toSpecification: false).Should().Equal(expected.Split(','));
        Codes(filter, toSpecification: true).Should().Equal(expected.Split(','));
    }

    /// <summary>The control: no bound, every row.</summary>
    [Fact]
    public void NoBound_KeepsEveryRow()
        => Codes("{}", toSpecification: false).Should().Equal("A", "B", "C");

    /// <summary>A <c>Range</c> whose name says neither bound would filter with equality, and is reported.</summary>
    [Fact]
    public void ARangeWhoseNameSaysNoBound_IsReported()
    {
        var result = Run(Entity + """

            [GridFilter<Order>]
            public partial class OrderGrid
            {
                [Filterable(Operators = FilterOps.Range, MapTo = "Amount")]
                public decimal? Amount { get; set; }
            }
            """);

        result.Diagnostics.Should().Contain(d => d.Id == "PRAG0703" && d.GetMessage().Contains("Amount"));
    }

    /// <summary>
    ///     The controls: named bounds are not reported, and neither is an undeclared <c>Operators</c>, which is <c>All</c>
    ///     and would make every <c>To…</c> a bound.
    /// </summary>
    [Fact]
    public void NamedBoundsAndAnUndeclaredOperators_AreNotReported()
    {
        Run(Entity + Grid).Diagnostics.Should().NotContain(d => d.Id == "PRAG0703");
        Run(Entity + """

            [GridFilter<Order>]
            public partial class OrderGrid
            {
                [Filterable]
                public string? Total { get; set; }
            }
            """).Diagnostics.Should().NotContain(d => d.Id == "PRAG0703");
    }

    private static List<string> Codes(string filterJson, bool toSpecification)
    {
        var assembly = Compiled.Value;
        var order = assembly.GetType("TestApp.Order")!;
        var grid = assembly.GetType("TestApp.OrderGrid")!;
        var filter = JsonSerializer.Deserialize(filterJson, grid, JsonSerializerOptions.Web)!;

        var rows = Array.CreateInstance(order, Rows.Length);
        for (var i = 0; i < Rows.Length; i++)
        {
            var row = Activator.CreateInstance(order)!;
            order.GetProperty("Code")!.SetValue(row, Rows[i].Code);
            order.GetProperty("Amount")!.SetValue(row, Rows[i].Amount);
            rows.SetValue(row, i);
        }

        IEnumerable applied;
        if (toSpecification)
        {
            var specification = grid.GetMethod("ToSpecification")!.Invoke(filter, [])!;
            var isSatisfiedBy = specification.GetType().GetMethod("IsSatisfiedBy")!;
            applied = ((IEnumerable)rows).Cast<object>().Where(row => (bool)isSatisfiedBy.Invoke(specification, [row])!).ToList();
        }
        else
        {
            var queryable = typeof(Queryable).GetMethods()
                .First(m => m.Name == nameof(Queryable.AsQueryable) && m.IsGenericMethod)
                .MakeGenericMethod(order)
                .Invoke(null, [rows])!;
            applied = (IEnumerable)grid.GetMethod("Apply")!.Invoke(filter, [queryable])!;
        }

        return [.. applied.Cast<object>().Select(o => (string)order.GetProperty("Code")!.GetValue(o)!)];
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source,
        [
            GeneratorTestHelper.FromType<GridFilterAttribute<object>>(),
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<FilterOperator>(),
            GeneratorTestHelper.FromType<Specification.Specification<object>>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Queryable)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Linq.Expressions.Expression)),
        ]);

    /// <remarks>
    ///     The reflection is the test harness's, not the framework's: the types being exercised exist only inside the
    ///     compilation the generator just produced.
    /// </remarks>
    private static Assembly Compile(string source)
    {
        using var stream = new MemoryStream();
        var emit = Run(source).OutputCompilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join("; ", emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())));

        return Assembly.Load(stream.ToArray());
    }
}
