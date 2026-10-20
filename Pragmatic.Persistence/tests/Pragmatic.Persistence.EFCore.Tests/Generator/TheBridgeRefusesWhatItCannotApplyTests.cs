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
///     The bridge <c>[GenerateGridBridge]</c> generates, <b>run</b>: a clause it cannot apply is refused, never dropped
///     (#325).
/// </summary>
/// <remarks>
///     <para>
///         A filter restricts, so a clause left out answers with more rows than the client asked for, under a success
///         status. The bridge refused a field it does not filter on, and for one it does, it dropped a value it could
///         not read, an operator the column's type does not support, and <c>In</c>/<c>Between</c>, which it never
///         generated. The grid adapters refuse all of them since #318; this is the same rule on the canonical path.
///     </para>
///     <para>
///         The compilation is emitted, loaded, and <c>ApplyCanonical</c> invoked over a request deserialized from
///         JSON, the only way an endpoint gets one: the existing tests compare the generated text.
///     </para>
/// </remarks>
public class TheBridgeRefusesWhatItCannotApplyTests
{
    private const string Source = """
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        [GenerateGridBridge]
        public class Order
        {
            [Filterable] public string Code { get; set; } = "";
            [Filterable] public decimal Amount { get; set; }
            [Filterable] public int? Rank { get; set; }
            [Filterable] public bool Paid { get; set; }
        }
        """;

    private static readonly (string Code, decimal Amount, int? Rank, bool Paid)[] Rows =
    [
        ("A-1", 10m, 1, true),
        ("B-2", 20m, null, false),
        ("A-3", 30m, 3, false),
    ];

    private static readonly Lazy<Assembly> Bridge = new(Compile);

    [Theory]
    [InlineData("""{"field":"Amount","operator":"Equals","value":"abc"}""")]
    [InlineData("""{"field":"Amount","operator":"Equals","value":[1,2]}""")]
    [InlineData("""{"field":"Amount","operator":"Equals","value":null}""")]
    [InlineData("""{"field":"Code","operator":"Contains","value":null}""")]
    [InlineData("""{"field":"Rank","operator":"GreaterThan","value":null}""")]
    [InlineData("""{"field":"Rank","operator":"Equals","value":99999999999}""")]
    [InlineData("""{"field":"Amount","operator":"In","value":[]}""")]
    [InlineData("""{"field":"Amount","operator":"In","value":[10,"abc"]}""")]
    [InlineData("""{"field":"Amount","operator":"Between","value":[10]}""")]
    public void AValueTheColumnCannotBeComparedWith_IsRefused(string clause)
        => Refusal(clause).Rejection.Should().Be(GridFilterRejection.UnusableValue);

    [Theory]
    [InlineData("""{"field":"Amount","operator":"StartsWith","value":"1"}""")]
    [InlineData("""{"field":"Paid","operator":"GreaterThan","value":true}""")]
    [InlineData("""{"field":"Code","operator":"Between","value":["A","B"]}""")]
    public void AnOperatorTheColumnDoesNotSupport_IsRefused(string clause)
        => Refusal(clause).Rejection.Should().Be(GridFilterRejection.UnsupportedOperator);

    /// <summary><c>In</c> and <c>Between</c>, which the bridge never generated and dropped, now select their rows.</summary>
    [Theory]
    [InlineData("""{"field":"Amount","operator":"In","value":[10,30]}""", "A-1,A-3")]
    [InlineData("""{"field":"Amount","operator":"Between","value":[15,35]}""", "B-2,A-3")]
    [InlineData("""{"field":"Code","operator":"In","value":["B-2"]}""", "B-2")]
    public void InAndBetween_SelectTheirRows(string clause, string expected)
        => Codes(clause).Should().Equal(expected.Split(','));

    /// <summary>
    ///     <c>FilterClause.Value</c> "may be null for is-null checks", and the bridge dropped every such clause: equality
    ///     against null is a comparison on a column that can hold it.
    /// </summary>
    [Theory]
    [InlineData("""{"field":"Rank","operator":"Equals","value":null}""", "B-2")]
    [InlineData("""{"field":"Rank","operator":"NotEquals","value":null}""", "A-1,A-3")]
    [InlineData("""{"field":"Code","operator":"Equals","value":null}""", "")]
    public void AnEqualityAgainstNull_IsApplied(string clause, string expected)
        => Codes(clause).Should().Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The control: usable clauses still select what they name, or the refusals are an off switch.</summary>
    [Theory]
    [InlineData("""{"field":"Code","operator":"StartsWith","value":"A"}""", "A-1,A-3")]
    [InlineData("""{"field":"Amount","operator":"GreaterOrEqual","value":20}""", "B-2,A-3")]
    [InlineData("""{"field":"Paid","operator":"Equals","value":true}""", "A-1")]
    public void AUsableClause_StillApplies(string clause, string expected)
        => Codes(clause).Should().Equal(expected.Split(','));

    private static GridFilterRejectedException Refusal(string clause)
    {
        var act = () => Codes(clause);

        var thrown = act.Should().Throw<TargetInvocationException>().Which.InnerException;
        thrown.Should().BeOfType<GridFilterRejectedException>();
        return (GridFilterRejectedException)thrown!;
    }

    private static List<string> Codes(string clause)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var request = JsonSerializer.Deserialize<GridFilterRequest>($$"""{"filters":[{{clause}}]}""", options)!;

        var assembly = Bridge.Value;
        var order = assembly.GetType("TestApp.Order")!;
        var bridge = assembly.GetType("TestApp.OrderGridFilterBridge")!;

        var rows = Array.CreateInstance(order, Rows.Length);
        for (var i = 0; i < Rows.Length; i++)
        {
            var row = Activator.CreateInstance(order)!;
            order.GetProperty("Code")!.SetValue(row, Rows[i].Code);
            order.GetProperty("Amount")!.SetValue(row, Rows[i].Amount);
            order.GetProperty("Rank")!.SetValue(row, Rows[i].Rank);
            order.GetProperty("Paid")!.SetValue(row, Rows[i].Paid);
            rows.SetValue(row, i);
        }

        var queryable = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.AsQueryable) && m.IsGenericMethod)
            .MakeGenericMethod(order)
            .Invoke(null, [rows])!;
        var applied = (IEnumerable)bridge.GetMethod("ApplyCanonical")!.Invoke(null, [queryable, request])!;

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
            GeneratorTestHelper.FromType<GenerateGridBridgeAttribute>(),
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<FilterOperator>(),
            GeneratorTestHelper.FromType<GridFilterRequest>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(JsonSerializer)),
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
