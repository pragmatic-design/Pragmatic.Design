using System.Globalization;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Expressions;
using Pragmatic.Documents.Templating.Pipes;

namespace Pragmatic.Documents.Templating.Tests;

/// <summary>
/// Integration tests for user-supplied <see cref="ITemplatePipe"/> implementations registered
/// via <see cref="PipeRegistry.With"/> and consumed both directly and through the evaluator.
/// </summary>
public class CustomPipeTests
{
    /// <summary>Reverses a string; ignores args.</summary>
    private sealed class ReversePipe : ITemplatePipe
    {
        public string Name => "reverse";

        public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
        {
            if (input is null) return null;
            var chars = input.ToString()!.ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }
    }

    /// <summary>Repeats the input N times (N from the first arg, default 2).</summary>
    private sealed class RepeatPipe : ITemplatePipe
    {
        public string Name => "repeat";

        public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
        {
            var count = args.Count > 0 && int.TryParse(args[0], out var n) ? n : 2;
            return string.Concat(Enumerable.Repeat(input?.ToString() ?? "", count));
        }
    }

    /// <summary>Captures the culture it was invoked with so tests can assert propagation.</summary>
    private sealed class CultureCapturePipe : ITemplatePipe
    {
        public string Name => "captureCulture";
        public CultureInfo? Captured { get; private set; }

        public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
        {
            Captured = culture;
            return culture.Name;
        }
    }

    /// <summary>Overrides the built-in "uppercase" pipe name to verify last-wins registration.</summary>
    private sealed class ShoutPipe : ITemplatePipe
    {
        public string Name => "uppercase";
        public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
            => $"{input?.ToString()?.ToUpperInvariant()}!!!";
    }

    [Fact]
    public void With_CustomPipe_RegistersUnderItsName()
    {
        var registry = PipeRegistry.Default.With(new ReversePipe());

        registry.Contains("reverse").Should().BeTrue();
    }

    [Fact]
    public void With_DoesNotMutateOriginalRegistry()
    {
        var original = PipeRegistry.Default;

        var extended = original.With(new ReversePipe());

        extended.Contains("reverse").Should().BeTrue();
        original.Contains("reverse").Should().BeFalse();
    }

    [Fact]
    public void Execute_CustomPipe_TransformsInput()
    {
        var registry = PipeRegistry.Default.With(new ReversePipe());

        var result = registry.Execute("reverse", "abc", [], CultureInfo.InvariantCulture);

        result.Should().Be("cba");
    }

    [Fact]
    public void Execute_UnknownPipe_Throws()
    {
        var act = () => PipeRegistry.Default.Execute("nope", "x", [], CultureInfo.InvariantCulture);

        act.Should().Throw<InvalidOperationException>().WithMessage("*nope*");
    }

    [Fact]
    public void Contains_IsCaseInsensitive()
    {
        var registry = PipeRegistry.Default.With(new ReversePipe());

        registry.Contains("REVERSE").Should().BeTrue();
        registry.Contains("Reverse").Should().BeTrue();
    }

    [Fact]
    public void With_PipeReusingBuiltInName_Throws()
    {
        // The registry is keyed by pipe name (case-insensitive); colliding with a built-in
        // name is rejected at construction rather than silently shadowing it.
        var act = () => PipeRegistry.Default.With(new ShoutPipe());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Evaluate_CustomPipe_AppliedInExpression()
    {
        var registry = PipeRegistry.Default.With(new ReversePipe());
        var eval = new ExpressionEvaluator(registry);
        var ctx = new TemplateDataContext().AddSource("word", "hello");
        var expr = ExpressionParser.ParseExpression("word | reverse");

        var result = await eval.EvaluateAsync(expr, ctx);

        result.Should().Be("olleh");
    }

    [Fact]
    public async Task Evaluate_CustomPipe_ReceivesArgs()
    {
        var registry = PipeRegistry.Default.With(new RepeatPipe());
        var eval = new ExpressionEvaluator(registry);
        var ctx = new TemplateDataContext().AddSource("x", "ab");
        var expr = ExpressionParser.ParseExpression("x | repeat:3");

        var result = await eval.EvaluateAsync(expr, ctx);

        result.Should().Be("ababab");
    }

    [Fact]
    public async Task Evaluate_CustomPipe_DefaultArgsWhenNoneSupplied()
    {
        var registry = PipeRegistry.Default.With(new RepeatPipe());
        var eval = new ExpressionEvaluator(registry);
        var ctx = new TemplateDataContext().AddSource("x", "z");
        var expr = ExpressionParser.ParseExpression("x | repeat");

        var result = await eval.EvaluateAsync(expr, ctx);

        result.Should().Be("zz");
    }

    [Fact]
    public async Task Evaluate_CustomPipe_ReceivesContextCulture()
    {
        var capture = new CultureCapturePipe();
        var registry = PipeRegistry.Default.With(capture);
        var eval = new ExpressionEvaluator(registry);
        var ctx = new TemplateDataContext().WithCulture("fr-FR").AddSource("x", "v");
        var expr = ExpressionParser.ParseExpression("x | captureCulture");

        var result = await eval.EvaluateAsync(expr, ctx);

        result.Should().Be("fr-FR");
        capture.Captured!.Name.Should().Be("fr-FR");
    }

    [Fact]
    public async Task Evaluate_CustomPipe_ChainsWithBuiltIn()
    {
        var registry = PipeRegistry.Default.With(new ReversePipe());
        var eval = new ExpressionEvaluator(registry);
        var ctx = new TemplateDataContext().AddSource("word", "abc");
        // reverse "abc" -> "cba", then built-in uppercase -> "CBA"
        var expr = ExpressionParser.ParseExpression("word | reverse | uppercase");

        var result = await eval.EvaluateAsync(expr, ctx);

        result.Should().Be("CBA");
    }

    [Fact]
    public async Task Evaluate_CustomPipe_NullInput_ReturnsNull()
    {
        var registry = PipeRegistry.Default.With(new ReversePipe());
        var eval = new ExpressionEvaluator(registry);
        var ctx = new TemplateDataContext().AddSource("word", (object?)null);
        var expr = ExpressionParser.ParseExpression("word | reverse");

        var result = await eval.EvaluateAsync(expr, ctx);

        result.Should().BeNull();
    }
}
