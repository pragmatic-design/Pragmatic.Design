using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Expressions;

namespace Pragmatic.Documents.Templating.Tests;

public class ExpressionEvaluatorTests
{
    private readonly ExpressionEvaluator _eval = new();

    private static TemplateDataContext Ctx(params (string name, object? value)[] sources)
    {
        var ctx = new TemplateDataContext();
        foreach (var (name, value) in sources) ctx.AddSource(name, value);
        return ctx;
    }

    private static IEnumerable<int> Infinite()
    {
        var i = 0;
        while (true) yield return i++;
    }

    [Fact]
    public async Task Evaluate_AggregateOverInfiniteSource_ThrowsInsteadOfLoopingForever()
    {
        var ctx = Ctx(("items", Infinite()));
        var expr = ExpressionParser.ParseExpression("items.count");

        var act = async () => await _eval.EvaluateAsync(expr, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*maximum*");
    }

    private sealed class TypeExposer
    {
        private readonly Type _t = typeof(string);
        public Type TheType => _t;
    }

    [Fact]
    public async Task Evaluate_ReflectionEscalationToAssembly_IsBlocked()
    {
        // A property that returns a Type is reachable, but reflecting further (.assembly) off framework
        // types must be denied — no walking from a data value into assembly/type metadata.
        var ctx = Ctx(("x", new TypeExposer()));
        var expr = ExpressionParser.ParseExpression("x.theType.assembly");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Evaluate_ExcessivelyDeepPropertyPath_Throws()
    {
        var ctx = Ctx(("x", new Dictionary<string, object?>()));
        var expr = ExpressionParser.ParseExpression("x." + string.Join('.', Enumerable.Repeat("a", 40)));

        var act = async () => await _eval.EvaluateAsync(expr, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*maximum depth*");
    }

    // --- Scalar Binding ---

    [Fact]
    public async Task Evaluate_PropertyAccess_ReturnsValue()
    {
        var ctx = Ctx(("customer", new Dictionary<string, object?> { ["name"] = "Mario" }));
        var expr = ExpressionParser.ParseExpression("customer.name");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("Mario");
    }

    [Fact]
    public async Task Evaluate_NestedPropertyAccess()
    {
        var ctx = Ctx(("customer", new Dictionary<string, object?>
        {
            ["address"] = new Dictionary<string, object?> { ["city"] = "Milano" }
        }));
        var expr = ExpressionParser.ParseExpression("customer.address.city");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("Milano");
    }

    [Fact]
    public async Task Evaluate_MissingProperty_ReturnsNull()
    {
        var ctx = Ctx(("customer", new Dictionary<string, object?> { ["name"] = "Mario" }));
        var expr = ExpressionParser.ParseExpression("customer.email");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().BeNull();
    }

    // --- Binary Operators ---

    [Fact]
    public async Task Evaluate_Addition()
    {
        var ctx = Ctx(("a", 10), ("b", 20));
        var expr = ExpressionParser.ParseExpression("a + b");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be(30.0);
    }

    [Fact]
    public async Task Evaluate_StringConcat()
    {
        var ctx = Ctx(("first", "Mario"), ("last", "Rossi"));
        var expr = ExpressionParser.ParseExpression("first + \" \" + last");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("Mario Rossi");
    }

    [Fact]
    public async Task Evaluate_Comparison_GreaterThan()
    {
        var ctx = Ctx(("total", 1500));
        var expr = ExpressionParser.ParseExpression("total > 1000");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be(true);
    }

    [Fact]
    public async Task Evaluate_Equality()
    {
        var ctx = Ctx(("status", "active"));
        var expr = ExpressionParser.ParseExpression("status == \"active\"");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be(true);
    }

    // --- Ternary ---

    [Fact]
    public async Task Evaluate_Ternary_TrueBranch()
    {
        var ctx = Ctx(("vip", true));
        var expr = ExpressionParser.ParseExpression("vip ? \"VIP\" : \"Standard\"");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("VIP");
    }

    [Fact]
    public async Task Evaluate_Ternary_FalseBranch()
    {
        var ctx = Ctx(("vip", false));
        var expr = ExpressionParser.ParseExpression("vip ? \"VIP\" : \"Standard\"");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("Standard");
    }

    // --- Null Coalescing ---

    [Fact]
    public async Task Evaluate_NullCoalescing_UsesLeft()
    {
        var ctx = Ctx(("name", "Mario"));
        var expr = ExpressionParser.ParseExpression("name ?? \"Unknown\"");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("Mario");
    }

    [Fact]
    public async Task Evaluate_NullCoalescing_UsesFallback()
    {
        var ctx = Ctx(("name", (object?)null));
        var expr = ExpressionParser.ParseExpression("name ?? \"Unknown\"");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("Unknown");
    }

    // --- Aggregates ---

    [Fact]
    public async Task Evaluate_Sum()
    {
        var items = new List<Dictionary<string, object?>>
        {
            new() { ["price"] = 100.0 },
            new() { ["price"] = 250.0 },
            new() { ["price"] = 50.0 }
        };
        var ctx = Ctx(("items", items));
        var expr = ExpressionParser.ParseExpression("items.sum(price)");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be(400.0);
    }

    [Fact]
    public async Task Evaluate_Count()
    {
        var items = new List<Dictionary<string, object?>> { new(), new(), new() };
        var ctx = Ctx(("items", items));
        var expr = ExpressionParser.ParseExpression("items.count");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be(3);
    }

    // --- Pipes ---

    [Fact]
    public async Task Evaluate_UppercasePipe()
    {
        var ctx = Ctx(("name", "mario"));
        var expr = ExpressionParser.ParseExpression("name | uppercase");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("MARIO");
    }

    [Fact]
    public async Task Evaluate_NumberPipe()
    {
        var ctx = Ctx(("price", 1234.5));
        ctx.WithCulture("en-US");
        var expr = ExpressionParser.ParseExpression("price | number:2");

        var result = await _eval.EvaluateAsync(expr, ctx);

        // Number formatting is locale-dependent; verify it contains the key digits
        var str = result!.ToString()!;
        str.Should().Contain("234");
        str.Should().Contain("50");
    }

    [Fact]
    public async Task Evaluate_DefaultPipe_WithValue()
    {
        var ctx = Ctx(("name", "Mario"));
        var expr = ExpressionParser.ParseExpression("name | default:\"N/A\"");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("Mario");
    }

    [Fact]
    public async Task Evaluate_DefaultPipe_WithNull()
    {
        var ctx = Ctx(("name", (object?)null));
        var expr = ExpressionParser.ParseExpression("name | default:\"N/A\"");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("N/A");
    }

    // --- Interpolated String ---

    [Fact]
    public async Task Evaluate_InterpolatedString()
    {
        var ctx = Ctx(("name", "Mario"), ("total", 99));
        var expr = ExpressionParser.ParseTemplate("Ciao {{name}}, totale: {{total}}");

        var result = await _eval.EvaluateToStringAsync(expr, ctx);

        result.Should().Be("Ciao Mario, totale: 99");
    }

    // --- Translation (placeholder) ---

    [Fact]
    public async Task Evaluate_Translation_NoLocalizer_ReturnsBracketedKey()
    {
        var ctx = new TemplateDataContext();
        var expr = ExpressionParser.ParseExpression("t:invoice.greeting");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("[invoice.greeting]");
    }

    // --- Bool evaluation ---

    [Fact]
    public async Task EvaluateToBool_TruthyValues()
    {
        var eval = _eval;

        (await eval.EvaluateToBoolAsync(new LiteralExpression(true), new TemplateDataContext())).Should().BeTrue();
        (await eval.EvaluateToBoolAsync(new LiteralExpression(1), new TemplateDataContext())).Should().BeTrue();
        (await eval.EvaluateToBoolAsync(new LiteralExpression("text"), new TemplateDataContext())).Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateToBool_FalsyValues()
    {
        var eval = _eval;

        (await eval.EvaluateToBoolAsync(new LiteralExpression(false), new TemplateDataContext())).Should().BeFalse();
        (await eval.EvaluateToBoolAsync(new LiteralExpression(null), new TemplateDataContext())).Should().BeFalse();
        (await eval.EvaluateToBoolAsync(new LiteralExpression(0), new TemplateDataContext())).Should().BeFalse();
        (await eval.EvaluateToBoolAsync(new LiteralExpression(""), new TemplateDataContext())).Should().BeFalse();
    }

    // --- Async DataSource ---

    [Fact]
    public async Task Evaluate_AsyncDataSource()
    {
        var ctx = new TemplateDataContext()
            .AddSource("order", async ct =>
            {
                await Task.Delay(1, ct);
                return (object?)new Dictionary<string, object?> { ["number"] = "ORD-001" };
            });

        var expr = ExpressionParser.ParseExpression("order.number");

        var result = await _eval.EvaluateAsync(expr, ctx);

        result.Should().Be("ORD-001");
    }

    // --- Multi-source ---

    [Fact]
    public async Task Evaluate_MultipleDataSources()
    {
        var ctx = new TemplateDataContext()
            .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Mario" })
            .AddSource("company", new Dictionary<string, object?> { ["name"] = "Pragmatic" });

        var expr = ExpressionParser.ParseTemplate("{{customer.name}} @ {{company.name}}");

        var result = await _eval.EvaluateToStringAsync(expr, ctx);

        result.Should().Be("Mario @ Pragmatic");
    }
}
