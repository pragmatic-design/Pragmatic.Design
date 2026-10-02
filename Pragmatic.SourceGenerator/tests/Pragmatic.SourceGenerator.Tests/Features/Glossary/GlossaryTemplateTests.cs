using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Glossary.Models;
using Pragmatic.SourceGenerator.Features.Glossary.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Glossary;

/// <summary>
///     The glossary generator emits PragmaticGlossary.Markdown — domain
///     terms ([Entity] types) grouped by namespace with their XML-doc summaries — as a compile-time
///     constant the consumer can surface (e.g. served like the generated OpenAPI).
/// </summary>
public class GlossaryTemplateTests
{
    [Fact]
    public void Glossary_GroupsTermsByNamespaceWithSummaries()
    {
        var entries = new[]
        {
            new GlossaryEntryModel { Name = "Order", Namespace = "Sales", Summary = "A customer order." },
            new GlossaryEntryModel { Name = "Customer", Namespace = "Sales", Summary = null },
            new GlossaryEntryModel { Name = "Invoice", Namespace = "Billing", Summary = "A bill to settle." }
        }.ToEquatableArray();

        var source = new GlossaryTemplate(entries, "MyApp").RenderOutput().Text;

        source.Should().Contain("namespace MyApp.Generated;");
        source.Should().Contain("public const string Markdown");
        source.Should().Contain("# Glossary");
        source.Should().Contain("## Sales");
        source.Should().Contain("**Order** — A customer order.");
        source.Should().Contain("**Customer**", "an undocumented term still appears, without a summary dash");
        source.Should().Contain("## Billing");
        source.Should().Contain("**Invoice** — A bill to settle.");
    }

    [Fact]
    public void Glossary_EscapesDoubleQuotesForVerbatimLiteral()
    {
        var entries = new[]
        {
            new GlossaryEntryModel { Name = "Quote", Namespace = "Sales", Summary = "A \"firm\" price." }
        }.ToEquatableArray();

        var source = new GlossaryTemplate(entries, "MyApp").RenderOutput().Text;

        // Double quotes in the content must be doubled so the verbatim string compiles.
        source.Should().Contain("A \"\"firm\"\" price.");
    }
}
