using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Documents.Templates;

namespace Pragmatic.Documents.Markup.Tests;

/// <summary>
/// A source added with its type known at compile time is read through the accessors the generator
/// writes for that type and for the types reached from it, so the reflective fallback never sees them.
/// The generator runs on this test project (see the csproj).
/// </summary>
public class ATypedTemplateIsReadWithoutReflectionTests
{
    private const string Markup = """
        <document title="Invoice">
            <page>
                <heading level="1">Invoice {{invoice.number}}</heading>
                <text>For {{invoice.customer.name}}</text>
                <table data-source="invoice.lines">
                    <column width="100">Description</column>
                    <column width="40" align="right">Amount</column>
                    <row-template>
                        <cell>{{item.description}}</cell>
                        <cell>{{item.amount}}</cell>
                    </row-template>
                </table>
            </page>
        </document>
        """;

    [Fact]
    public async Task ATypedSource_RendersWithoutReachingReflection()
    {
        var invoice = new Invoice
        {
            Number = "2026-7",
            Customer = new Customer { Name = "Ada" },
            Lines = [new Line { Description = "Consulting", Amount = 120 }]
        };
        var context = new TemplateDataContext().AddSource("invoice", invoice);

        var model = await new DocumentTemplateResolver(PipeRegistry.Default)
            .ResolveAsync(PdxDocParser.Parse(Markup), context);

        var rendered = JsonSerializer.Serialize(model);
        rendered.Should().Contain("Invoice 2026-7");
        rendered.Should().Contain("For Ada");
        rendered.Should().Contain("Consulting");
        context.Warnings.Should().BeEmpty();

        ReflectionPropertyAccessor.HasRead(typeof(Invoice)).Should().BeFalse();
        ReflectionPropertyAccessor.HasRead(typeof(Customer)).Should().BeFalse();
        ReflectionPropertyAccessor.HasRead(typeof(Line)).Should().BeFalse();
    }

    [Fact]
    public async Task APropertyTheTypeDoesNotHave_IsAWarning_AndStillNoReflection()
    {
        var context = new TemplateDataContext().AddSource("customer", new Customer { Name = "Ada" });

        var value = await context.ResolveAsync("customer.vatId");

        value.Should().BeNull();
        context.Warnings.Should().HaveCount(1);
        ReflectionPropertyAccessor.HasRead(typeof(Customer)).Should().BeFalse();
    }

    /// <summary>
    /// The control: an anonymous type cannot be named in generated code, so it is still read by reflection,
    /// which is what shows that <see cref="ReflectionPropertyAccessor.HasRead"/> sees a reflective read.
    /// </summary>
    [Fact]
    public async Task AnAnonymousSource_IsStillReadByReflection()
    {
        var anonymous = new { Number = "2026-8" };
        var context = new TemplateDataContext().AddSource("invoice", anonymous);

        var value = await context.ResolveAsync("invoice.number");

        value.Should().Be("2026-8");
        ReflectionPropertyAccessor.HasRead(anonymous.GetType()).Should().BeTrue();
    }

    public sealed class Invoice
    {
        public string Number { get; init; } = "";
        public Customer Customer { get; init; } = new();
        public IReadOnlyList<Line> Lines { get; init; } = [];
    }

    public sealed class Customer
    {
        public string Name { get; init; } = "";
    }

    public sealed class Line
    {
        public string Description { get; init; } = "";
        public decimal Amount { get; init; }
    }
}
