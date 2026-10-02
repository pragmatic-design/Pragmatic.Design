using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Expressions;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Documents.Templating.I18N.Tests;

public class TranslationResolverTests
{
    [Fact]
    public async Task Translate_SimpleKey_ResolvesViaLocalizer()
    {
        var localizer = new TestStringLocalizer(new Dictionary<string, string>
        {
            ["invoice.title"] = "Fattura",
            ["invoice.greeting"] = "Gentile cliente"
        });

        var ctx = new TemplateDataContext().WithLocalizer(localizer);
        var evaluator = new ExpressionEvaluator();

        var expr = ExpressionParser.ParseExpression("t:invoice.title");
        var result = await evaluator.EvaluateAsync(expr, ctx);

        result.Should().Be("Fattura");
    }

    [Fact]
    public async Task Translate_WithParams_PassesArgsToLocalizer()
    {
        // Template parameters are NAMED: the resolver interpolates {name} from the
        // parameter dictionary (index-stable, independent of IStringLocalizer impl),
        // rather than relying on positional {0} + dictionary enumeration order.
        var localizer = new TestStringLocalizer(new Dictionary<string, string>
        {
            ["greeting"] = "Ciao {name}, benvenuto!"
        });

        var ctx = new TemplateDataContext()
            .WithLocalizer(localizer)
            .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Mario" });

        var evaluator = new ExpressionEvaluator();
        var expr = ExpressionParser.ParseExpression("t:greeting(name=customer.name)");
        var result = await evaluator.EvaluateAsync(expr, ctx);

        result.Should().Be("Ciao Mario, benvenuto!");
    }

    [Fact]
    public async Task Translate_MissingKey_ReturnsKeyAsFallback()
    {
        var localizer = new TestStringLocalizer([]);

        var ctx = new TemplateDataContext().WithLocalizer(localizer);
        var evaluator = new ExpressionEvaluator();

        var expr = ExpressionParser.ParseExpression("t:missing.key");
        var result = await evaluator.EvaluateAsync(expr, ctx);

        // TranslationResult.Missing returns the key as value
        result!.ToString().Should().Be("missing.key");
    }

    [Fact]
    public async Task Translate_InInterpolatedString()
    {
        var localizer = new TestStringLocalizer(new Dictionary<string, string>
        {
            ["invoice.total_label"] = "Totale"
        });

        var ctx = new TemplateDataContext()
            .WithLocalizer(localizer)
            .AddSource("total", "€5.000,00");

        var evaluator = new ExpressionEvaluator();
        var expr = ExpressionParser.ParseTemplate("{{t:invoice.total_label}}: {{total}}");
        var result = await evaluator.EvaluateToStringAsync(expr, ctx);

        result.Should().Be("Totale: €5.000,00");
    }

    [Fact]
    public void I18NPipes_RegisteredCorrectly()
    {
        var registry = PipeRegistry.Default.WithI18N();

        registry.Contains("date").Should().BeTrue();
        registry.Contains("currency").Should().BeTrue();
        registry.Contains("percent").Should().BeTrue();
    }

    [Fact]
    public async Task FullPipeline_DateFormatted_WithTranslation()
    {
        var localizer = new TestStringLocalizer(new Dictionary<string, string>
        {
            ["invoice.date_label"] = "Data fattura"
        });

        var pipes = PipeRegistry.Default.WithI18N();
        var evaluator = new ExpressionEvaluator(pipes);
        var ctx = new TemplateDataContext()
            .WithLocalizer(localizer)
            .WithCulture("it-IT")
            .AddSource("invoice", new Dictionary<string, object?>
            {
                ["date"] = new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.Zero)
            });

        var expr = ExpressionParser.ParseTemplate("{{t:invoice.date_label}}: {{invoice.date | date:\"dd/MM/yyyy\"}}");
        var result = await evaluator.EvaluateToStringAsync(expr, ctx);

        result.Should().Be("Data fattura: 09/04/2026");
    }

    /// <summary>
    ///     The document's language governs <c>t:</c> as it governs the pipes. A resolver that asked the
    ///     localizer in the ambient culture would print, for a context set to Italian, Italian dates inside
    ///     English sentences unless the caller also opened a culture scope around the render.
    /// </summary>
    [Fact]
    public async Task Translate_InTheContextsCulture_NotTheAmbientOne()
    {
        var localizer = new TwoLanguageLocalizer("en", new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new() { ["invoice.date_label"] = "Invoice date" },
            ["it-IT"] = new() { ["invoice.date_label"] = "Data fattura" },
        });

        var ctx = new TemplateDataContext().WithLocalizer(localizer).WithCulture("it-IT");
        var evaluator = new ExpressionEvaluator();

        var result = await evaluator.EvaluateAsync(ExpressionParser.ParseExpression("t:invoice.date_label"), ctx);

        result.Should().Be("Data fattura");
    }

    /// <summary>The control: a context with no culture set keeps asking in the ambient one.</summary>
    [Fact]
    public async Task Translate_WithNoCultureSet_UsesTheAmbientOne()
    {
        var localizer = new TwoLanguageLocalizer("en", new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new() { ["invoice.date_label"] = "Invoice date" },
            ["it-IT"] = new() { ["invoice.date_label"] = "Data fattura" },
        });

        var ctx = new TemplateDataContext().WithLocalizer(localizer);
        var evaluator = new ExpressionEvaluator();

        var result = await evaluator.EvaluateAsync(ExpressionParser.ParseExpression("t:invoice.date_label"), ctx);

        result.Should().Be("Invoice date");
    }

    /// <summary>A localizer whose indexer answers in its own culture, and whose WithCulture switches it.</summary>
    private sealed class TwoLanguageLocalizer(
        string culture, Dictionary<string, Dictionary<string, string>> byCulture) : IStringLocalizer
    {
        public string Culture => culture;

        public TranslationResult this[string key] =>
            byCulture[culture].TryGetValue(key, out var value)
                ? TranslationResult.Found(key, value)
                : TranslationResult.Missing(key);

        public TranslationResult this[string key, params object[] args] => this[key];
        public TranslationResult Plural(string key, int count) => this[key];
        public TranslationResult Plural(string key, int count, params object[] args) => this[key];
        public IStringLocalizer WithCulture(string target) => new TwoLanguageLocalizer(target, byCulture);
    }

    /// <summary>Minimal IStringLocalizer for testing.</summary>
    private sealed class TestStringLocalizer(Dictionary<string, string> translations) : IStringLocalizer
    {
        public string Culture => "it-IT";

        public TranslationResult this[string key] =>
            translations.TryGetValue(key, out var value)
                ? TranslationResult.Found(key, value)
                : TranslationResult.Missing(key);

        public TranslationResult this[string key, params object[] args]
        {
            get
            {
                if (!translations.TryGetValue(key, out var template))
                    return TranslationResult.Missing(key);

                var formatted = string.Format(template, args);
                return TranslationResult.Found(key, formatted);
            }
        }

        public TranslationResult Plural(string key, int count) => this[key];
        public TranslationResult Plural(string key, int count, params object[] args) => this[key, args];
        public IStringLocalizer WithCulture(string culture) => this;
    }
}
