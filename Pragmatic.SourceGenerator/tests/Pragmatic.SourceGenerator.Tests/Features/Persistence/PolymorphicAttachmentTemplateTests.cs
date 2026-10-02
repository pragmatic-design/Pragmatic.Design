using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class PolymorphicAttachmentTemplateTests
{
    [Fact]
    public void RenderOutput_GeneratesOwnerProperties()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("OwnerType");
        source.Should().Contain("OwnerId");
    }

    [Fact]
    public void RenderOutput_GeneratesPartialClass()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("partial class Document");
    }

    [Fact]
    public void RenderOutput_GeneratesForOwnerExtension()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("ForOwner<TOwner>");
        source.Should().Contain("where TOwner : class");
        // Discriminate by full type name to avoid same-name cross-namespace collisions.
        source.Should().Contain("typeof(TOwner).FullName");
        source.Should().NotContain("typeof(TOwner).Name;");
    }

    [Fact]
    public void RenderOutput_GeneratesExtensionsClass()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("DocumentAttachmentExtensions");
    }

    [Fact]
    public void RenderOutput_GeneratesTypedConvenienceMethods()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("ForInvoice(");
        source.Should().Contain("ForReservation(");
        source.Should().Contain("ForOwner<global::Sales.Invoice>()");
        source.Should().Contain("ForOwner<global::Sales.Reservation>()");
    }

    private static string Render(PolymorphicAttachmentModel model)
    {
        var template = new PolymorphicAttachmentTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static PolymorphicAttachmentModel BuildModel()
    {
        return new PolymorphicAttachmentModel
        {
            Namespace = "Sales",
            TypeName = "Document",
            FullTypeName = "global::Sales.Document",
            OwnerTypes = ImmutableArray.Create(
                new OwnerTypeModel { TypeName = "Invoice", FullTypeName = "global::Sales.Invoice" },
                new OwnerTypeModel { TypeName = "Reservation", FullTypeName = "global::Sales.Reservation" })
        };
    }
}
