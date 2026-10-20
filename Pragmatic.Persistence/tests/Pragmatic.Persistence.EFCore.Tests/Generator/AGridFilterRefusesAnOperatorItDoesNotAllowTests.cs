using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     The <c>Apply</c> a <c>[GridFilter&lt;T&gt;]</c> generates, <b>run</b>: an operator a property does not allow is
///     refused, not applied as equality (#328).
/// </summary>
/// <remarks>
///     <para>
///         The operator switch ended in a default that applied the documented default operator: <c>Contains</c> for
///         text, <c>==</c> for anything else. That default is right for an operator the client did not send, and it
///         also caught every operator the property does not allow, <c>In</c> and <c>Between</c> included: a
///         <c>GreaterThan</c> on an equality-only property answered with the rows equal to the value, under a success
///         status. The rule the grid paths follow since #318: refused, a 400 from the host.
///     </para>
///     <para>
///         The filter is deserialized from JSON, as an endpoint binds it, and the operators travel as names.
///     </para>
/// </remarks>
public class AGridFilterRefusesAnOperatorItDoesNotAllowTests
{
    private const string Source = """
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public class Order
        {
            public string Code { get; set; } = "";
            public decimal Amount { get; set; }
        }

        [GridFilter<Order>]
        public partial class OrderGrid
        {
            [Filterable(Operators = FilterOps.String)]
            public string? Code { get; set; }
            public StringOperator? CodeOperator { get; set; }

            [Filterable(Operators = FilterOps.Equality)]
            public decimal? Amount { get; set; }
            public FilterOperator? AmountOperator { get; set; }
        }
        """;

    private static readonly (string Code, decimal Amount)[] Rows = [("A-1", 10m), ("B-2", 20m), ("A-3", 30m)];

    private static readonly Lazy<Assembly> Compiled = new(Compile);

    [Theory]
    [InlineData("""{"amount":20,"amountOperator":"GreaterThan"}""", "Amount", "GreaterThan")]
    [InlineData("""{"amount":20,"amountOperator":"In"}""", "Amount", "In")]
    [InlineData("""{"amount":20,"amountOperator":"Between"}""", "Amount", "Between")]
    [InlineData("""{"code":"A-1","codeOperator":"Equals"}""", "Code", "Equals")]
    public void AnOperatorThePropertyDoesNotAllow_IsRefused(string filter, string field, string op)
    {
        var act = () => Codes(filter, toSpecification: false);

        var thrown = act.Should().Throw<TargetInvocationException>().Which.InnerException;
        thrown.Should().BeOfType<GridFilterRejectedException>();
        thrown.Should().Match<GridFilterRejectedException>(e =>
            e.Rejection == GridFilterRejection.UnsupportedOperator && e.Field == field && e.Operator == op);
    }

    /// <summary><c>ToSpecification</c> reads the same operators, and refuses the same ones.</summary>
    [Fact]
    public void ToSpecification_RefusesTheSameOperator()
    {
        var act = () => Codes("""{"amount":20,"amountOperator":"LessThan"}""", toSpecification: true);

        act.Should().Throw<TargetInvocationException>().Which.InnerException.Should().BeOfType<GridFilterRejectedException>();
    }

    /// <summary>An operator the client did not send is the documented default: <c>Contains</c> for text, <c>==</c> otherwise.</summary>
    [Theory]
    [InlineData("""{"code":"A"}""", "A-1,A-3")]
    [InlineData("""{"amount":20}""", "B-2")]
    public void NoOperator_IsTheDefault(string filter, string expected)
    {
        Codes(filter, toSpecification: false).Should().Equal(expected.Split(','));
        Codes(filter, toSpecification: true).Should().Equal(expected.Split(','));
    }

    /// <summary>The control: an allowed operator still applies.</summary>
    [Theory]
    [InlineData("""{"code":"A-","codeOperator":"StartsWith"}""", "A-1,A-3")]
    [InlineData("""{"amount":20,"amountOperator":"NotEquals"}""", "A-1,A-3")]
    public void AnAllowedOperator_StillApplies(string filter, string expected)
    {
        Codes(filter, toSpecification: false).Should().Equal(expected.Split(','));
        Codes(filter, toSpecification: true).Should().Equal(expected.Split(','));
    }

    private static List<string> Codes(string filterJson, bool toSpecification)
    {
        var assembly = Compiled.Value;
        var order = assembly.GetType("TestApp.Order")!;
        var grid = assembly.GetType("TestApp.OrderGrid")!;

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var filter = JsonSerializer.Deserialize(filterJson, grid, options)!;

        var rows = Array.CreateInstance(order, Rows.Length);
        for (var i = 0; i < Rows.Length; i++)
        {
            var row = Activator.CreateInstance(order)!;
            order.GetProperty("Code")!.SetValue(row, Rows[i].Code);
            order.GetProperty("Amount")!.SetValue(row, Rows[i].Amount);
            rows.SetValue(row, i);
        }

        var queryable = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.AsQueryable) && m.IsGenericMethod)
            .MakeGenericMethod(order)
            .Invoke(null, [rows])!;

        IEnumerable applied;
        if (toSpecification)
        {
            // The specification as a predicate over the same rows.
            var specification = grid.GetMethod("ToSpecification")!.Invoke(filter, [])!;
            var isSatisfiedBy = specification.GetType().GetMethod("IsSatisfiedBy")!;
            applied = ((IEnumerable)rows).Cast<object>().Where(row => (bool)isSatisfiedBy.Invoke(specification, [row])!).ToList();
        }
        else
        {
            applied = (IEnumerable)grid.GetMethod("Apply")!.Invoke(filter, [queryable])!;
        }

        return [.. applied.Cast<object>().Select(o => (string)order.GetProperty("Code")!.GetValue(o)!)];
    }

    /// <remarks>
    ///     The reflection is the test harness's, not the framework's: the types being exercised exist only inside the
    ///     compilation the generator just produced.
    /// </remarks>
    private static Assembly Compile()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source,
        [
            GeneratorTestHelper.FromType<GridFilterAttribute<object>>(),
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<FilterOperator>(),
            GeneratorTestHelper.FromType<Specification.Specification<object>>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Queryable)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Linq.Expressions.Expression)),
        ]);

        using var stream = new MemoryStream();
        var emit = result.OutputCompilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join("; ", emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())));

        return Assembly.Load(stream.ToArray());
    }
}
