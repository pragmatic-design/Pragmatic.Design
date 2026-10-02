using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Tests for the <see cref="GeneratedValueTemplate" /> app-side formatter codegen.
/// </summary>
public class GeneratedValueTemplateTests
{
    private static string Render(string format) =>
        new GeneratedValueTemplate(new GeneratedValueModel
        {
            EntityFullName = "MyApp.Sales.Order",
            EntityShortName = "Order",
            Namespace = "MyApp.Sales",
            PropertyName = "OrderNumber",
            GeneratorClassName = "OrderOrderNumberValueGenerator",
            SetterName = "SetOrderNumber",
            Segments = GeneratedValueFormat.Parse(format)
        }).RenderOutput().Text;

    [Fact]
    public void Emits_Generator_Implementing_IDefaultValueGenerator()
    {
        var source = Render("ORD-{YYYY}{MM}-{RANDOM:4}");

        source.Should().Contain("class OrderOrderNumberValueGenerator");
        source.Should().Contain("global::Pragmatic.Persistence.Lifecycle.IDefaultValueGenerator<global::MyApp.Sales.Order, string>");
        source.Should().Contain("global::System.Threading.Tasks.Task<string> GenerateAsync");
    }

    [Fact]
    public void Emits_Literal_And_Date_Parts()
    {
        var source = Render("ORD-{YYYY}{MM}{DD}");

        source.Should().Contain("__sb.Append(\"ORD-\");");
        source.Should().Contain("context.Now.Year.ToString(\"D4\"");
        source.Should().Contain("context.Now.Month.ToString(\"D2\"");
        source.Should().Contain("context.Now.Day.ToString(\"D2\"");
    }

    [Fact]
    public void Emits_TwoDigitYear()
    {
        Render("{YY}").Should().Contain("(context.Now.Year % 100).ToString(\"D2\"");
    }

    [Fact]
    public void Emits_Random_WithHelper()
    {
        var source = Render("{RANDOM:8}");

        source.Should().Contain("__sb.Append(__Random(8));");
        source.Should().Contain("private static string __Random(int count)");
        source.Should().Contain("global::System.Random.Shared.Next");
    }

    [Fact]
    public void Emits_Guid_Clamped_To_32()
    {
        Render("{GUID:6}").Should().Contain("global::System.Guid.NewGuid().ToString(\"N\").Substring(0, 6)");
        // Widths above a GUID's 32 chars are clamped so Substring never throws.
        Render("{GUID:40}").Should().Contain("Substring(0, 32)");
    }

    [Fact]
    public void NoRandomToken_OmitsHelper()
    {
        Render("INV-{YYYY}").Should().NotContain("__Random");
    }

    [Fact]
    public void SequenceToken_InjectsKeyedDbContext_And_FetchesFromDbSequence()
    {
        // {SEQ:N} is backed by a real DB sequence — the generator injects the boundary-keyed
        // DbContext and awaits SequenceValueProvider.NextAsync, zero-padded to the token width.
        var source = new GeneratedValueTemplate(new GeneratedValueModel
        {
            EntityFullName = "MyApp.Billing.Invoice",
            EntityShortName = "Invoice",
            Namespace = "MyApp.Billing",
            PropertyName = "InvoiceNumber",
            GeneratorClassName = "InvoiceInvoiceNumberValueGenerator",
            SetterName = "SetInvoiceNumber",
            Segments = GeneratedValueFormat.Parse("INV-{YYYY}-{SEQ:5}"),
            HasSequence = true,
            SequenceName = "Invoice_InvoiceNumber_seq",
            TargetBoundaryFullName = "MyApp.Billing.BillingBoundary"
        }).RenderOutput().Text;

        source.Should().Contain("[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof(global::MyApp.Billing.BillingBoundary))]");
        source.Should().Contain("async global::System.Threading.Tasks.Task<string> GenerateAsync");
        source.Should().Contain("global::Pragmatic.Persistence.EFCore.Sequences.SequenceValueProvider.NextAsync(_db, \"Invoice_InvoiceNumber_seq\", ct)");
        source.Should().Contain(".ToString(\"D5\", global::System.Globalization.CultureInfo.InvariantCulture)");
    }
}
