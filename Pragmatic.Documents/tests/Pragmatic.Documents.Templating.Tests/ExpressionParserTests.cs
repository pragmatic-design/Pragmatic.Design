using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Templating.Expressions;

namespace Pragmatic.Documents.Templating.Tests;

public class ExpressionParserTests
{
    // --- Literals ---

    [Fact]
    public void Parse_StringLiteral()
    {
        var expr = ExpressionParser.ParseExpression("\"hello\"");
        expr.Should().BeOfType<LiteralExpression>().Which.Value.Should().Be("hello");
    }

    [Fact]
    public void Parse_IntLiteral()
    {
        var expr = ExpressionParser.ParseExpression("42");
        expr.Should().BeOfType<LiteralExpression>().Which.Value.Should().Be(42);
    }

    [Fact]
    public void Parse_DoubleLiteral()
    {
        var expr = ExpressionParser.ParseExpression("3.14");
        expr.Should().BeOfType<LiteralExpression>().Which.Value.Should().Be(3.14);
    }

    [Fact]
    public void Parse_LongLiteral_BeyondIntRange()
    {
        // Larger than int.MaxValue but within long range → parsed as long.
        var expr = ExpressionParser.ParseExpression("9999999999");
        expr.Should().BeOfType<LiteralExpression>().Which.Value.Should().Be(9999999999L);
    }

    [Fact]
    public void Parse_HugeIntegerLiteral_FallsBackToDouble()
    {
        // A 19+ digit integer overflows long → must fall back to double, not throw OverflowException.
        var act = () => ExpressionParser.ParseExpression("99999999999999999999");
        act.Should().NotThrow();

        var expr = ExpressionParser.ParseExpression("99999999999999999999");
        expr.Should().BeOfType<LiteralExpression>().Which.Value.Should().BeOfType<double>();
    }

    [Fact]
    public void Parse_BoolTrue()
    {
        var expr = ExpressionParser.ParseExpression("true");
        expr.Should().BeOfType<LiteralExpression>().Which.Value.Should().Be(true);
    }

    [Fact]
    public void Parse_Null()
    {
        var expr = ExpressionParser.ParseExpression("null");
        expr.Should().BeOfType<LiteralExpression>().Which.Value.Should().BeNull();
    }

    // --- Property Access ---

    [Fact]
    public void Parse_SimpleProperty()
    {
        var expr = ExpressionParser.ParseExpression("name");
        expr.Should().BeOfType<PropertyAccessExpression>().Which.Path.Should().Be("name");
    }

    [Fact]
    public void Parse_DottedPath()
    {
        var expr = ExpressionParser.ParseExpression("customer.address.city");
        expr.Should().BeOfType<PropertyAccessExpression>().Which.Path.Should().Be("customer.address.city");
    }

    // --- Pipes ---

    [Fact]
    public void Parse_SinglePipe()
    {
        var expr = ExpressionParser.ParseExpression("name | uppercase");
        var pipe = expr.Should().BeOfType<PipeExpression>().Subject;
        pipe.PipeName.Should().Be("uppercase");
        pipe.Input.Should().BeOfType<PropertyAccessExpression>().Which.Path.Should().Be("name");
    }

    [Fact]
    public void Parse_PipeWithArgs()
    {
        var expr = ExpressionParser.ParseExpression("total | number:2");
        var pipe = expr.Should().BeOfType<PipeExpression>().Subject;
        pipe.PipeName.Should().Be("number");
        pipe.Args.Should().BeEquivalentTo(["2"]);
    }

    [Fact]
    public void Parse_PipeWithStringArg()
    {
        var expr = ExpressionParser.ParseExpression("date | date:\"dd/MM/yyyy\"");
        var pipe = expr.Should().BeOfType<PipeExpression>().Subject;
        pipe.PipeName.Should().Be("date");
        pipe.Args.Should().BeEquivalentTo(["dd/MM/yyyy"]);
    }

    [Fact]
    public void Parse_ChainedPipes()
    {
        var expr = ExpressionParser.ParseExpression("name | trim | uppercase");
        var outer = expr.Should().BeOfType<PipeExpression>().Subject;
        outer.PipeName.Should().Be("uppercase");
        var inner = outer.Input.Should().BeOfType<PipeExpression>().Subject;
        inner.PipeName.Should().Be("trim");
    }

    // --- Binary Operators ---

    [Fact]
    public void Parse_Addition()
    {
        var expr = ExpressionParser.ParseExpression("a + b");
        var bin = expr.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOp.Add);
    }

    [Fact]
    public void Parse_Comparison()
    {
        var expr = ExpressionParser.ParseExpression("total > 1000");
        var bin = expr.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOp.Gt);
        bin.Right.Should().BeOfType<LiteralExpression>().Which.Value.Should().Be(1000);
    }

    [Fact]
    public void Parse_Equality()
    {
        var expr = ExpressionParser.ParseExpression("status == \"active\"");
        var bin = expr.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOp.Eq);
    }

    [Fact]
    public void Parse_LogicalAnd()
    {
        var expr = ExpressionParser.ParseExpression("a && b");
        var bin = expr.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOp.And);
    }

    // --- Ternary ---

    [Fact]
    public void Parse_Ternary()
    {
        var expr = ExpressionParser.ParseExpression("active ? \"Yes\" : \"No\"");
        var tern = expr.Should().BeOfType<TernaryExpression>().Subject;
        tern.TrueValue.Should().BeOfType<LiteralExpression>().Which.Value.Should().Be("Yes");
        tern.FalseValue.Should().BeOfType<LiteralExpression>().Which.Value.Should().Be("No");
    }

    // --- Null Coalescing ---

    [Fact]
    public void Parse_NullCoalescing()
    {
        var expr = ExpressionParser.ParseExpression("middleName ?? \"\"");
        expr.Should().BeOfType<NullCoalescingExpression>();
    }

    // --- Aggregates ---

    [Fact]
    public void Parse_Sum()
    {
        var expr = ExpressionParser.ParseExpression("items.sum(price)");
        var agg = expr.Should().BeOfType<AggregateExpression>().Subject;
        agg.CollectionPath.Should().Be("items");
        agg.Function.Should().Be(AggregateFunction.Sum);
        agg.PropertyName.Should().Be("price");
    }

    [Fact]
    public void Parse_Count()
    {
        var expr = ExpressionParser.ParseExpression("items.count");
        var agg = expr.Should().BeOfType<AggregateExpression>().Subject;
        agg.Function.Should().Be(AggregateFunction.Count);
        agg.PropertyName.Should().BeNull();
    }

    // --- Translation ---

    [Fact]
    public void Parse_TranslationSimple()
    {
        var expr = ExpressionParser.ParseExpression("t:invoice.greeting");
        var tr = expr.Should().BeOfType<TranslateExpression>().Subject;
        tr.Key.Should().Be("invoice.greeting");
        tr.Params.Should().BeNull();
    }

    [Fact]
    public void Parse_TranslationWithParams()
    {
        var expr = ExpressionParser.ParseExpression("t:greeting(name=customer.name)");
        var tr = expr.Should().BeOfType<TranslateExpression>().Subject;
        tr.Key.Should().Be("greeting");
        tr.Params.Should().ContainKey("name");
        tr.Params!["name"].Should().BeOfType<PropertyAccessExpression>().Which.Path.Should().Be("customer.name");
    }

    // --- Interpolated Template ---

    [Fact]
    public void ParseTemplate_PureLiteral()
    {
        var expr = ExpressionParser.ParseTemplate("Hello World");
        expr.Should().BeOfType<LiteralExpression>().Which.Value.Should().Be("Hello World");
    }

    [Fact]
    public void ParseTemplate_SingleExpression()
    {
        var expr = ExpressionParser.ParseTemplate("{{name}}");
        expr.Should().BeOfType<PropertyAccessExpression>().Which.Path.Should().Be("name");
    }

    [Fact]
    public void ParseTemplate_Mixed()
    {
        var expr = ExpressionParser.ParseTemplate("Hello {{name}}, your total is {{total | number:2}}");
        var interp = expr.Should().BeOfType<InterpolatedStringExpression>().Subject;
        interp.Segments.Should().HaveCount(4);
        interp.Segments[0].Should().BeOfType<TextSegment>().Which.Text.Should().Be("Hello ");
        interp.Segments[1].Should().BeOfType<ExpressionSegment>();
        interp.Segments[2].Should().BeOfType<TextSegment>().Which.Text.Should().Be(", your total is ");
        interp.Segments[3].Should().BeOfType<ExpressionSegment>();
    }

    [Fact]
    public void ParseTemplate_UnclosedBraces_Throws()
    {
        var act = () => ExpressionParser.ParseTemplate("Hello {{name");
        act.Should().Throw<TemplateParseException>();
    }

    [Fact]
    public void ParseExpression_DeeplyNestedParentheses_ThrowsInsteadOfStackOverflow()
    {
        // A modest amount of nesting would overflow an unguarded recursive-descent parser.
        var expr = new string('(', 5000) + "1" + new string(')', 5000);
        var act = () => ExpressionParser.ParseExpression(expr);
        act.Should().Throw<TemplateParseException>().WithMessage("*depth*");
    }

    [Fact]
    public void ParseExpression_DeeplyNestedNullCoalesce_ThrowsInsteadOfStackOverflow()
    {
        var expr = string.Concat(Enumerable.Repeat("a ?? ", 5000)) + "b";
        var act = () => ExpressionParser.ParseExpression(expr);
        act.Should().Throw<TemplateParseException>().WithMessage("*depth*");
    }

    [Fact]
    public void ParseExpression_MalformedNumber_ThrowsTemplateParseException()
    {
        var act = () => ExpressionParser.ParseExpression("1.2.3");
        act.Should().Throw<TemplateParseException>().WithMessage("*number*");
    }
}
