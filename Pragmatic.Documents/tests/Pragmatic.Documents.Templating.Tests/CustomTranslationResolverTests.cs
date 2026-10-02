using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Expressions;

namespace Pragmatic.Documents.Templating.Tests;

/// <summary>
/// Integration tests for a user-supplied <see cref="ITranslationResolver"/> wired via
/// <see cref="TemplateDataContext.WithTranslationResolver"/>, independent of the I18N
/// (IStringLocalizer) package. Verifies the <c>t:key</c> expression path and the
/// bracketed-key fallback when no resolver is registered.
/// </summary>
public class CustomTranslationResolverTests
{
    /// <summary>Dictionary-backed resolver that interpolates <c>{name}</c> named parameters.</summary>
    private sealed class DictionaryTranslationResolver(IReadOnlyDictionary<string, string> table) : ITranslationResolver
    {
        public string Resolve(string key, IReadOnlyDictionary<string, object?>? parameters, System.Globalization.CultureInfo culture)
        {
            if (!table.TryGetValue(key, out var template))
                return key; // missing-key fallback: echo the key

            if (parameters is null)
                return template;

            foreach (var (name, value) in parameters)
                template = template.Replace($"{{{name}}}", value?.ToString() ?? "", StringComparison.Ordinal);

            return template;
        }
    }

    /// <summary>Records the arguments it was called with for assertion.</summary>
    private sealed class RecordingTranslationResolver : ITranslationResolver
    {
        public string? LastKey { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastParameters { get; private set; }

        public string Resolve(string key, IReadOnlyDictionary<string, object?>? parameters, System.Globalization.CultureInfo culture)
        {
            LastKey = key;
            LastParameters = parameters;
            return $"<{key}>";
        }
    }

    private static readonly ExpressionEvaluator Eval = new();

    [Fact]
    public async Task Translate_SimpleKey_ResolvedByCustomResolver()
    {
        var resolver = new DictionaryTranslationResolver(new Dictionary<string, string>
        {
            ["invoice.title"] = "Invoice"
        });
        var ctx = new TemplateDataContext().WithTranslationResolver(resolver);
        var expr = ExpressionParser.ParseExpression("t:invoice.title");

        var result = await Eval.EvaluateAsync(expr, ctx);

        result.Should().Be("Invoice");
    }

    [Fact]
    public async Task Translate_WithNamedParams_InterpolatedByCustomResolver()
    {
        var resolver = new DictionaryTranslationResolver(new Dictionary<string, string>
        {
            ["greeting"] = "Hello {name}!"
        });
        var ctx = new TemplateDataContext()
            .WithTranslationResolver(resolver)
            .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Alice" });
        var expr = ExpressionParser.ParseExpression("t:greeting(name=customer.name)");

        var result = await Eval.EvaluateAsync(expr, ctx);

        result.Should().Be("Hello Alice!");
    }

    [Fact]
    public async Task Translate_MissingKey_CustomResolverEchoesKey()
    {
        var resolver = new DictionaryTranslationResolver(new Dictionary<string, string>());
        var ctx = new TemplateDataContext().WithTranslationResolver(resolver);
        var expr = ExpressionParser.ParseExpression("t:absent.key");

        var result = await Eval.EvaluateAsync(expr, ctx);

        result.Should().Be("absent.key");
    }

    [Fact]
    public async Task Translate_NoResolver_ReturnsBracketedKey()
    {
        var ctx = new TemplateDataContext();
        var expr = ExpressionParser.ParseExpression("t:invoice.title");

        var result = await Eval.EvaluateAsync(expr, ctx);

        result.Should().Be("[invoice.title]");
    }

    [Fact]
    public async Task Translate_NoResolverWithParams_ReturnsBracketedKeyWithParams()
    {
        var ctx = new TemplateDataContext()
            .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Bob" });
        var expr = ExpressionParser.ParseExpression("t:greeting(name=customer.name)");

        var result = await Eval.EvaluateAsync(expr, ctx);

        result!.ToString().Should().Be("[greeting(name=Bob)]");
    }

    [Fact]
    public async Task Translate_ResolverReceivesResolvedParameterValues()
    {
        var recorder = new RecordingTranslationResolver();
        var ctx = new TemplateDataContext()
            .WithTranslationResolver(recorder)
            .AddSource("order", new Dictionary<string, object?> { ["id"] = 42 });
        var expr = ExpressionParser.ParseExpression("t:order.label(num=order.id)");

        await Eval.EvaluateAsync(expr, ctx);

        recorder.LastKey.Should().Be("order.label");
        recorder.LastParameters.Should().ContainKey("num");
        recorder.LastParameters!["num"].Should().Be(42);
    }

    [Fact]
    public async Task Translate_ResolverInheritedByChildContext()
    {
        var resolver = new DictionaryTranslationResolver(new Dictionary<string, string>
        {
            ["k"] = "value"
        });
        var parent = new TemplateDataContext().WithTranslationResolver(resolver);
        var child = parent.CreateChildScope("item", new Dictionary<string, object?>());
        var expr = ExpressionParser.ParseExpression("t:k");

        var result = await Eval.EvaluateAsync(expr, child);

        result.Should().Be("value");
    }

    [Fact]
    public async Task Translate_InInterpolatedString_UsesCustomResolver()
    {
        var resolver = new DictionaryTranslationResolver(new Dictionary<string, string>
        {
            ["label.total"] = "Total"
        });
        var ctx = new TemplateDataContext()
            .WithTranslationResolver(resolver)
            .AddSource("amount", "100");
        var expr = ExpressionParser.ParseTemplate("{{t:label.total}}: {{amount}}");

        var result = await Eval.EvaluateToStringAsync(expr, ctx);

        result.Should().Be("Total: 100");
    }
}
