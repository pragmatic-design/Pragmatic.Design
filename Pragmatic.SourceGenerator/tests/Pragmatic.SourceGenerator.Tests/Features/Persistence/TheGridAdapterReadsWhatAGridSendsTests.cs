using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The adapter <c>[GridAdapter&lt;T&gt;]</c> generates, <b>run</b> over the load options a
///     grid actually sends.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The existing tests pin the emitted text.</b> They would keep passing while the adapter
///         filtered nothing, which is what it did: the generated parser tests <c>filter[0] is string</c>
///         and a filter that arrived over HTTP is a <c>JsonElement</c>, so the whole branch was dead and
///         <c>Apply</c> returned the query untouched. Measured against Showcase's property grid: a
///         filter for one seeded city returned the entire page.
///     </para>
///     <para>
///         So the compilation is emitted, loaded, and the generated <c>Apply</c> is invoked — over a
///         <c>DevExpressLoadOptions</c> <b>deserialised from JSON</b>, because that is the only way one
///         ever exists. The same arrangement as
///         <c>Features.FastEnum.FastEnumGeneratedAssemblyFixture</c>.
///     </para>
/// </remarks>
public class TheGridAdapterReadsWhatAGridSendsTests
{
    private const string Source = """
        namespace Sample
        {
            using Pragmatic.Persistence.Query.Attributes;

            public class Order
            {
                public string Customer { get; set; } = "";
                public int Total { get; set; }
                public string TenantId { get; set; } = "";
            }

            [GridAdapter<Order>(Framework = GridFramework.DevExpress)]
            [GridExclude("TenantId")]
            public partial class OrderGrid;
        }
        """;

    /// <remarks>
    ///     ⚠️ <c>Pragmatic.Persistence.EFCore</c> is in the list because the adapter's output is gated on
    ///     it (<c>QueryFeature</c>: <c>if (!features.HasPersistenceEFCore) return;</c>). Without it the
    ///     generator emits nothing at all and the partial class comes out empty — and these tests fail
    ///     saying the adapter has no <c>Apply</c>.
    /// </remarks>
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<GridAdapterAttribute<object>>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(JsonSerializer)),
        // ⚠️ Queryable lives in its own assembly, apart from Enumerable. Without it `query.Skip(n)` in
        // the generated adapter binds to Enumerable.Skip and the file does not compile — which reads
        // exactly like a defect in the generator and is a defect in this list.
        GeneratorTestHelper.FromTypeAssembly(typeof(System.Linq.Queryable)),
    ];

    [Fact]
    public void AFilterThatArrivedAsJson_IsApplied()
    {
        var grid = Adapter();

        var kept = grid.Apply(
            [("Ada", 10, "t1"), ("Grace", 20, "t1")],
            """{"filter":["customer","=","Ada"],"skip":0,"take":20}""");

        kept.Should().ContainSingle().Which.Should().Be("Ada");
    }

    /// <summary>
    ///     The control: with no filter, both rows come back.
    /// </summary>
    /// <remarks>
    ///     Without it, "the filter is applied" is satisfied by an adapter that returns one row whatever
    ///     it is asked, and by one that returns nothing at all — which is the other way this can break.
    /// </remarks>
    [Fact]
    public void WithNoFilter_EveryRowComesBack()
    {
        var grid = Adapter();

        grid.Apply([("Ada", 10, "t1"), ("Grace", 20, "t1")], """{"skip":0,"take":20}""")
            .Should().HaveCount(2);
    }

    /// <summary>A comparison on a number, which arrives as a JSON number rather than a string.</summary>
    [Fact]
    public void AComparisonOnANumber_IsApplied()
    {
        var grid = Adapter();

        grid.Apply(
                [("Ada", 10, "t1"), ("Grace", 20, "t1")],
                """{"filter":["total",">",15],"skip":0,"take":20}""")
            .Should().ContainSingle().Which.Should().Be("Grace");
    }

    /// <summary>
    ///     A column the adapter excludes cannot be filtered on: the clause is refused, and a column with a sensitive
    ///     name is refused as withheld, as the bridge refuses it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is what <c>[GridExclude]</c> is for and it is why the generated adapter is worth
    ///     having at all: the runtime one resolves any name against the entity, so a tenant column is
    ///     reachable by whoever types it. The clause used to be ignored, so the answer was the unfiltered page
    ///     under a success status, which the grid shows as filtered (#318).
    /// </remarks>
    [Fact]
    public void AnExcludedColumn_IsRefused()
    {
        var act = () => Adapter().Apply(
            [("Ada", 10, "t1"), ("Grace", 20, "t2")],
            """{"filter":["tenantId","=","t2"],"skip":0,"take":20}""");

        Refusal<GridFieldRejectedException>(act).Rejection.Should().Be(GridFieldRejection.Withheld);
    }

    /// <summary>A sort the grid sends is applied, in the direction it asked for.</summary>
    [Fact]
    public void ASortThatArrivedAsJson_IsApplied()
    {
        var grid = Adapter();

        grid.Apply(
                [("Ada", 10, "t1"), ("Grace", 20, "t1")],
                """{"sort":[{"selector":"total","desc":true}],"skip":0,"take":20}""")
            .Should().Equal("Grace", "Ada");
    }

    /// <summary>
    ///     A sort on a column the adapter does not know is skipped, and the next one is still the first.
    /// </summary>
    /// <remarks>
    ///     Found by the fuzz harness (#310): the loop marked the first sort as done whether or not it applied, so the
    ///     next became a <c>ThenBy</c> on a query nobody had ordered, and LINQ threw out of the adapter.
    /// </remarks>
    [Fact]
    public void ASortOnAnUnknownColumn_LeavesTheNextOneFirst()
    {
        var grid = Adapter();

        grid.Apply(
                [("Ada", 10, "t1"), ("Grace", 20, "t1")],
                """{"sort":[{"selector":"nope"},{"selector":"total","desc":true}]}""")
            .Should().Equal("Grace", "Ada");
    }

    /// <summary>
    ///     A column can be given a different name on the wire, which is what <c>[GridField]</c> is for.
    /// </summary>
    /// <remarks>
    ///     ⚠️ An alias added <b>beside</b> the column the transform already auto-discovered produces
    ///     <c>ApplySortTotal</c> and <c>CreateTotalFilter</c> twice — CS0111. One column, one pair of methods; the declaration wins and the discovered one
    ///     steps aside.
    /// </remarks>
    [Fact]
    public void AColumnRenamedOnTheWire_IsFilteredByItsNewName()
    {
        var grid = Adapter("""
            [GridAdapter<Order>(Framework = GridFramework.DevExpress)]
            [GridField("amount", Property = "Total")]
            public partial class OrderGrid;
            """);

        grid.Apply(
                [("Ada", 10, "t1"), ("Grace", 20, "t1")],
                """{"filter":["amount",">",15],"skip":0,"take":20}""")
            .Should().ContainSingle().Which.Should().Be("Grace");
    }

    /// <summary>
    ///     The control: once renamed, the column's own name is no longer one the grid can use.
    /// </summary>
    /// <remarks>
    ///     Otherwise "it was renamed" would be satisfied by an adapter that answers to both — which is
    ///     the state that does not compile, and which would make the rename a suggestion.
    /// </remarks>
    [Fact]
    public void OnceRenamed_TheOldNameIsNotAnswered()
    {
        var grid = Adapter("""
            [GridAdapter<Order>(Framework = GridFramework.DevExpress)]
            [GridField("amount", Property = "Total")]
            public partial class OrderGrid;
            """);

        var act = () => grid.Apply(
            [("Ada", 10, "t1"), ("Grace", 20, "t1")],
            """{"filter":["total",">",15],"skip":0,"take":20}""");

        Refusal<GridFieldRejectedException>(act).Should().Match<GridFieldRejectedException>(
            e => e.Field == "total" && e.Rejection == GridFieldRejection.Unknown,
            "the column answers to 'amount' now, and to nothing else");
    }

    /// <summary>
    ///     An unknown field inside an or is refused. The clause was skipped, and the or answered with whatever its
    ///     other half selected: rows the client did not ask for, as the filtered result (#318).
    /// </summary>
    [Fact]
    public void AnOrWithAnUnknownField_IsRefused()
    {
        var act = () => Adapter().Apply(
            [("Ada", 10, "t1"), ("Grace", 20, "t1")],
            """{"filter":[["customer","=","Ada"],"or",["nope","=",1]]}""");

        Refusal<GridFieldRejectedException>(act).Field.Should().Be("nope");
    }

    [Theory]
    [InlineData("""["total","=","abc"]""", "UnusableValue")]
    [InlineData("""["total","=",99999999999]""", "UnusableValue")]
    [InlineData("""["customer","contains",null]""", "UnusableValue")]
    [InlineData("""["total","=",null]""", "UnusableValue")]
    [InlineData("""["total","contains","1"]""", "UnsupportedOperator")]
    [InlineData("""["customer","bogus","Ada"]""", "UnsupportedOperator")]
    [InlineData("""["customer",3,"Ada"]""", "UnsupportedOperator")]
    [InlineData("""[["customer","=","Ada"],"xor",["total","=",10]]""", "Malformed")]
    [InlineData("""[["customer","=","Ada"],"or"]""", "Malformed")]
    [InlineData("""[["customer","=","Ada"],"or",7]""", "Malformed")]
    [InlineData("""[[]]""", "Malformed")]
    [InlineData("""["!",3]""", "Malformed")]
    public void AClauseTheAdapterCannotApply_IsRefused(string filter, string rejection)
    {
        var act = () => Adapter().Apply([("Ada", 10, "t1"), ("Grace", 20, "t1")], $$"""{"filter":{{filter}}}""");

        Refusal<GridFilterRejectedException>(act).Rejection.ToString().Should().Be(rejection);
    }

    /// <summary>
    ///     An operator outside a field's <c>AllowedOperators</c> is refused. The restriction closes the
    ///     <c>startswith</c> oracle, and a skipped clause answered with every row instead (#318).
    /// </summary>
    [Fact]
    public void AnOperatorTheFieldDoesNotAllow_IsRefused()
    {
        var grid = Adapter("""
            [GridAdapter<Order>(Framework = GridFramework.DevExpress)]
            [GridField("customer", Property = "Customer", AllowedOperators = new[] { "equals" })]
            public partial class OrderGrid;
            """);

        var act = () => grid.Apply([("Ada", 10, "t1"), ("Grace", 20, "t1")], """{"filter":["customer","startswith","A"]}""");

        Refusal<GridFilterRejectedException>(act).Should().Match<GridFilterRejectedException>(
            e => e.Rejection == GridFilterRejection.UnsupportedOperator && e.Operator == "startswith");
        grid.Apply([("Ada", 10, "t1"), ("Grace", 20, "t1")], """{"filter":["customer","=","Ada"]}""").Should().Equal("Ada");
    }

    /// <summary>
    ///     Equality against null is a comparison, as the runtime adapter makes it: a clause with a null value was
    ///     dropped, and <c>["customer","=",null]</c> answered with every row.
    /// </summary>
    [Theory]
    [InlineData("=", "")]
    [InlineData("<>", "Ada,Grace")]
    public void AnEqualityAgainstNull_IsApplied(string op, string expected)
        => Adapter().Apply([("Ada", 10, "t1"), ("Grace", 20, "t1")], $$"""{"filter":["customer","{{op}}",null]}""")
            .Should().Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A group of one clause is that clause, as the runtime adapter now reads it.</summary>
    [Fact]
    public void AGroupOfOneClause_IsReadAsTheClause()
        => Adapter().Apply([("Ada", 10, "t1"), ("Grace", 20, "t1")], """{"filter":[["total",">",15]]}""")
            .Should().Equal("Grace");

    /// <summary>A negation, which the runtime adapter applied and the generated one dropped with the whole filter.</summary>
    [Fact]
    public void ANegation_IsApplied()
        => Adapter().Apply([("Ada", 10, "t1"), ("Grace", 20, "t1")], """{"filter":["!",["customer","=","Ada"]]}""")
            .Should().Equal("Grace");

    /// <summary>The control: a usable or still selects what each half names.</summary>
    [Fact]
    public void AUsableOr_StillSelectsBothHalves()
        => Adapter().Apply(
                [("Ada", 10, "t1"), ("Grace", 20, "t1"), ("Linus", 30, "t1")],
                """{"filter":[["customer","=","Ada"],"OR",["total",">",25]]}""")
            .Should().Equal("Ada", "Linus");

    /// <summary>
    ///     A PrimeNG match mode the adapter does not know is refused. It was read as equals: <c>dateBefore</c>
    ///     answered with the rows on that date.
    /// </summary>
    [Fact]
    public void APrimeNGMatchModeTheAdapterDoesNotKnow_IsRefused()
    {
        var act = () => PrimeNGAdapter().ApplyPrimeNG(
            [("Ada", 10, "t1")], """{"filters":{"total":{"value":10,"matchMode":"dateBefore"}}}""");

        Refusal<GridFilterRejectedException>(act).Should().Match<GridFilterRejectedException>(
            e => e.Rejection == GridFilterRejection.UnsupportedOperator && e.Operator == "dateBefore");
    }

    /// <summary>In and between, which the runtime adapter applies (#317) and the generated one read as equals.</summary>
    [Theory]
    [InlineData("""{"filters":{"total":{"value":[10,30],"matchMode":"in"}}}""", "Ada,Linus")]
    [InlineData("""{"filters":{"total":{"value":[15,35],"matchMode":"between"}}}""", "Grace,Linus")]
    public void APrimeNGInOrBetween_IsApplied(string lazyEvent, string expected)
        => PrimeNGAdapter().ApplyPrimeNG([("Ada", 10, "t1"), ("Grace", 20, "t1"), ("Linus", 30, "t1")], lazyEvent)
            .Should().Equal(expected.Split(','));

    [Theory]
    [InlineData("""{"filters":{"nope":{"value":1,"matchMode":"equals"}}}""")]
    [InlineData("""{"globalFilter":"a","globalFilterFields":["customer","nope"]}""")]
    public void APrimeNGColumnTheAdapterDoesNotKnow_IsRefused(string lazyEvent)
    {
        var act = () => PrimeNGAdapter().ApplyPrimeNG([("Ada", 10, "t1")], lazyEvent);

        Refusal<GridFieldRejectedException>(act).Field.Should().Be("nope");
    }

    [Theory]
    [InlineData("""{"filters":{"total":{"value":[],"matchMode":"in"}}}""")]
    [InlineData("""{"filters":{"total":{"value":[1,2,3],"matchMode":"between"}}}""")]
    [InlineData("""{"globalFilter":"a","globalFilterFields":["total"]}""")]
    [InlineData("""{"globalFilter":"a"}""")]
    public void APrimeNGFilterThatSelectsNothingToFilterOn_IsRefused(string lazyEvent)
    {
        var act = () => PrimeNGAdapter().ApplyPrimeNG([("Ada", 10, "t1")], lazyEvent);

        Refusal<GridFilterRejectedException>(act).Rejection.Should().Be(GridFilterRejection.UnusableValue);
    }

    /// <summary>The control: a global filter over a text column, with a number column beside it, still applies.</summary>
    [Fact]
    public void APrimeNGGlobalFilter_StillApplies()
        => PrimeNGAdapter().ApplyPrimeNG(
                [("Ada", 10, "t1"), ("Grace", 20, "t1")], """{"globalFilter":"Gr","globalFilterFields":["customer","total"]}""")
            .Should().Equal("Grace");

    /// <summary>
    ///     A filter at <see cref="GridFilterTooLargeException.MaxTerms" /> terms is applied; one past it is
    ///     refused before the adapter builds anything (#310).
    /// </summary>
    /// <remarks>
    ///     The generated parser joins a composite's terms one by one, and every join rewrites the whole chain
    ///     built so far: 10,000 terms took 9 seconds in <c>Apply</c> alone, and the chain it left overflowed the
    ///     stack of whatever executed it. A stack overflow ends the process, not the request.
    /// </remarks>
    [Fact]
    public void AFilterAtTheBound_IsApplied_AndOnePastIt_IsRefused()
    {
        var grid = Adapter();
        var rows = Enumerable.Range(0, 300).Select(i => ($"c{i}", i, "t1")).ToArray();

        grid.Apply(rows, $$"""{"filter":[{{Ors(GridFilterTooLargeException.MaxTerms)}}]}""")
            .Should().HaveCount(GridFilterTooLargeException.MaxTerms);

        var act = () => grid.Apply(rows, $$"""{"filter":[{{Ors(GridFilterTooLargeException.MaxTerms + 1)}}]}""");
        act.Should().Throw<TargetInvocationException>().Where(e => e.InnerException is GridFilterTooLargeException);
    }

    /// <summary>A sort nests the query like a clause does: 10,000 of them overflowed the same way.</summary>
    [Fact]
    public void SortsPastTheBound_AreRefused()
    {
        var sorts = string.Join(",", Enumerable.Repeat("""{"selector":"total"}""", GridFilterTooLargeException.MaxTerms + 1));

        var act = () => Adapter().Apply([("Ada", 10, "t1")], $$"""{"sort":[{{sorts}}]}""");

        act.Should().Throw<TargetInvocationException>().Where(e => e.InnerException is GridFilterTooLargeException);
    }

    /// <summary>PrimeNG's global filter joins one predicate per column it names, and its sorts chain the same way.</summary>
    [Theory]
    [InlineData("globalFilterFields")]
    [InlineData("multiSortMeta")]
    public void APrimeNGRequestPastTheBound_IsRefused(string property)
    {
        var grid = Adapter("""
            [GridAdapter<Order>(Framework = GridFramework.PrimeNG)]
            public partial class OrderGrid;
            """);
        var items = property == "globalFilterFields"
            ? string.Join(",", Enumerable.Repeat("\"customer\"", GridFilterTooLargeException.MaxTerms + 1))
            : string.Join(",", Enumerable.Repeat("""{"field":"total"}""", GridFilterTooLargeException.MaxTerms + 1));

        var act = () => grid.ApplyPrimeNG([("Ada", 10, "t1")], $$"""{"globalFilter":"a","{{property}}":[{{items}}]}""");

        act.Should().Throw<TargetInvocationException>().Where(e => e.InnerException is GridFilterTooLargeException);
    }

    /// <summary>
    ///     A string that is not UTF-8 is refused where it stands, not an exception out of the adapter: as a field
    ///     name, a filter that is not one; as a value, an unusable one, and not a <c>null</c>.
    /// </summary>
    /// <remarks>
    ///     An element of <c>Filter</c> binds as a <c>JsonElement</c> whose text is validated only when read, and the
    ///     generated <c>Unwrap</c> read it with <c>GetString</c>, which throws on such bytes (#310). Read as null
    ///     after that, <c>["customer","&lt;&gt;",…]</c> was a clause on nothing (#318).
    /// </remarks>
    [Theory]
    [InlineData("[\"\u0001customer\",\"=\",\"Ada\"]", "Malformed")]
    [InlineData("[\"customer\",\"=\",{\"\u0001key\":1}]", "UnusableValue")]
    [InlineData("[\"customer\",\"<>\",\"\u0001Ada\"]", "UnusableValue")]
    public void AStringThatIsNotUtf8_IsRefused(string filter, string rejection)
    {
        // 0x9A, a continuation byte with nothing before it, where the 0x01 is.
        var body = Encoding.UTF8.GetBytes($"{{\"filter\":{filter}}}")
            .Select(b => b == 0x01 ? (byte)0x9A : b).ToArray();

        var act = () => Adapter().Apply([("Ada", 10, "t1")], body);

        Refusal<GridFilterRejectedException>(act).Rejection.ToString().Should().Be(rejection);
    }

    /// <summary>The refusal the generated code threw, out of the reflection call that ran it.</summary>
    private static TException Refusal<TException>(Func<IReadOnlyList<string>> act) where TException : Exception
    {
        var thrown = act.Should().Throw<TargetInvocationException>().Which.InnerException;
        thrown.Should().BeOfType<TException>();
        return (TException)thrown!;
    }

    private static GeneratedGrid PrimeNGAdapter() => Adapter("""
        [GridAdapter<Order>(Framework = GridFramework.PrimeNG)]
        [GridExclude("TenantId")]
        public partial class OrderGrid;
        """);

    private static string Ors(int terms)
        => string.Join(",\"or\",", Enumerable.Range(0, terms).Select(i => $"[\"total\",\"=\",{i}]"));

    private static GeneratedGrid Adapter(string? gridDeclaration = null)
    {
        var source = gridDeclaration is null
            ? Source
            : Source.Replace(
                """
                    [GridAdapter<Order>(Framework = GridFramework.DevExpress)]
                    [GridExclude("TenantId")]
                    public partial class OrderGrid;
                """.Trim(),
                gridDeclaration.Trim(),
                StringComparison.Ordinal);

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References);

        // ⚠️ The first error first, on one line: a red run's record keeps 160 characters of the message
        // and nothing else, so anything said before the datum is what survives instead of the datum.
        var errors = result.OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => (d.Id + " " + d.GetMessage()).ReplaceLineEndings(" "))
            .ToList();

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"{errors[0]} ({errors.Count} error(s) in the generated adapter)");

        using var stream = new MemoryStream();
        var emit = result.OutputCompilation.Emit(stream);

        emit.Success.Should().BeTrue(string.Join("; ", emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())));

        return new GeneratedGrid(Assembly.Load(stream.ToArray()));
    }

    /// <summary>
    ///     The generated assembly, with just enough reflection to build its rows and call its adapter.
    /// </summary>
    /// <remarks>
    ///     The reflection is the test harness's, not the framework's: the types being exercised exist
    ///     only inside the compilation the generator just produced.
    /// </remarks>
    private sealed class GeneratedGrid(Assembly assembly)
    {
        private readonly Type _order = assembly.GetType("Sample.Order")
            ?? throw new InvalidOperationException("Sample.Order is not in the generated assembly.");

        private readonly Type _grid = assembly.GetType("Sample.OrderGrid")
            ?? throw new InvalidOperationException("Sample.OrderGrid is not in the generated assembly.");

        /// <summary>The customers left, in order, after the grid's load options are applied.</summary>
        public IReadOnlyList<string> Apply(
            (string Customer, int Total, string TenantId)[] rows, string loadOptionsJson)
            => Apply(rows, Encoding.UTF8.GetBytes(loadOptionsJson));

        /// <summary>The same, from the bytes of the body, which may hold what a string cannot.</summary>
        public IReadOnlyList<string> Apply(
            (string Customer, int Total, string TenantId)[] rows, byte[] loadOptionsBody)
        {
            var options = JsonSerializer.Deserialize<DevExpressLoadOptions>(
                loadOptionsBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

            var apply = Array.Find(
                    _grid.GetMethods(BindingFlags.Public | BindingFlags.Static),
                    m => m.Name == "Apply"
                        && m.GetParameters() is [_, { ParameterType.Name: nameof(DevExpressLoadOptions) }])
                ?? throw new InvalidOperationException(
                    "The generated adapter has no DevExpress Apply. It has: "
                    + string.Join(", ", _grid.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Select(m => m.Name + "(" + string.Join(", ", m.GetParameters()
                            .Select(p => p.ParameterType.Name)) + ")")));

            var applied = (IEnumerable)apply.Invoke(null, [Queryable(rows), options])!;

            return [.. applied.Cast<object>().Select(o => (string)_order.GetProperty("Customer")!.GetValue(o)!)];
        }

        /// <summary>The customers left after a PrimeNG lazy-load event is applied.</summary>
        public IReadOnlyList<string> ApplyPrimeNG(
            (string Customer, int Total, string TenantId)[] rows, string lazyEventJson)
        {
            var lazyEvent = JsonSerializer.Deserialize<PrimeNGLazyLoadEvent>(
                lazyEventJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

            var apply = Array.Find(
                    _grid.GetMethods(BindingFlags.Public | BindingFlags.Static),
                    m => m.Name == "Apply"
                        && m.GetParameters() is [_, { ParameterType.Name: nameof(PrimeNGLazyLoadEvent) }])
                ?? throw new InvalidOperationException("The generated adapter has no PrimeNG Apply.");

            var applied = (IEnumerable)apply.Invoke(null, [Queryable(rows), lazyEvent])!;

            return [.. applied.Cast<object>().Select(o => (string)_order.GetProperty("Customer")!.GetValue(o)!)];
        }

        private object Queryable((string Customer, int Total, string TenantId)[] rows)
        {
            var list = Array.CreateInstance(_order, rows.Length);

            for (var i = 0; i < rows.Length; i++)
            {
                var row = Activator.CreateInstance(_order)!;
                _order.GetProperty("Customer")!.SetValue(row, rows[i].Customer);
                _order.GetProperty("Total")!.SetValue(row, rows[i].Total);
                _order.GetProperty("TenantId")!.SetValue(row, rows[i].TenantId);
                list.SetValue(row, i);
            }

            return typeof(System.Linq.Queryable)
                .GetMethods()
                .First(m => m.Name == "AsQueryable" && m.IsGenericMethod)
                .MakeGenericMethod(_order)
                .Invoke(null, [list])!;
        }
    }
}
