using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Documents.Models;
using Pragmatic.SourceGenerator.Features.Documents.Templates;

namespace Pragmatic.SourceGenerator.Tests.Features.Documents;

/// <summary>
/// Verify snapshot of the generated template accessor registration.
/// </summary>
public class TemplateAccessorsSnapshotTests
{
    [Fact]
    public Task AccessorRegistration_MatchesSnapshot()
    {
        ImmutableArray<TemplateDataTypeModel> types =
        [
            new("global::MyApp.Invoices.Invoice", ImmutableArray.Create("Number", "Customer", "Lines")),
            new("global::MyApp.Invoices.InvoiceLine", ImmutableArray.Create("Description", "Amount"))
        ];

        var source = new TemplateAccessorsTemplate("MyApp.Generated", types).RenderOutput().Text;

        return Verify(source);
    }
}
