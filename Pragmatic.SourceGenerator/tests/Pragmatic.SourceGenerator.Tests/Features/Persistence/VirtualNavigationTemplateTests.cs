using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class VirtualNavigationTemplateTests
{
    [Fact]
    public void RenderOutput_GeneratesQueryMethod()
    {
        var (owner, attachment) = BuildModel();
        var source = Render(owner, attachment);

        source.Should().Contain("QueryDocuments(");
        source.Should().Contain("this global::Sales.Invoice owner");
        source.Should().Contain("IReadRepository<global::Sales.Document>");
        source.Should().Contain("IQueryable<global::Sales.Document>");
    }

    [Fact]
    public void RenderOutput_FiltersOnOwnerType()
    {
        var (owner, attachment) = BuildModel();
        var source = Render(owner, attachment);

        // The discriminator is the FULL type name (matches typeof(Owner).FullName on the set side).
        source.Should().Contain("a.OwnerType == \"Sales.Invoice\"");
        source.Should().NotContain("a.OwnerType == \"Invoice\"");
        source.Should().Contain("a.OwnerId == owner.Id.ToString()");
    }

    [Fact]
    public void RenderOutput_GeneratesBatchMethod()
    {
        var (owner, attachment) = BuildModel();
        var source = Render(owner, attachment);

        source.Should().Contain("LoadDocumentsBatchAsync(");
        source.Should().Contain("IEnumerable<global::System.Guid> ownerIds");
        source.Should().Contain("ILookup<string, global::Sales.Document>");
    }

    [Fact]
    public void RenderOutput_GeneratesStaticExtensionClass()
    {
        var (owner, attachment) = BuildModel();
        var source = Render(owner, attachment);

        source.Should().Contain("public static class InvoiceDocumentNavigationExtensions");
    }

    [Fact]
    public void RenderOutput_UsesOwnerNamespace()
    {
        var (owner, attachment) = BuildModel();
        var source = Render(owner, attachment);

        // Should use the owner's namespace (Sales), not attachment's (Sales)
        source.Should().Contain("namespace Sales;");
    }

    [Fact]
    public void RenderOutput_HintNameIncludesOwnerAndAttachment()
    {
        var (owner, attachment) = BuildModel();
        var template = new VirtualNavigationTemplate(owner, attachment);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Contain("Invoice");
        artifact.HintName.Should().Contain("Document");
        artifact.HintName.Should().Contain("Navigation");
    }

    [Fact]
    public void Validate_MissingOwnerTypeName_DoesNotRender()
    {
        var owner = new OwnerTypeModel
        {
            TypeName = "",
            FullTypeName = ""
        };
        var attachment = new PolymorphicAttachmentModel
        {
            Namespace = "Sales",
            TypeName = "Document",
            FullTypeName = "global::Sales.Document"
        };

        var template = new VirtualNavigationTemplate(owner, attachment);
        var artifact = template.RenderOutput();

        // Validate() returns false → empty content (no class generated)
        artifact.Text.Should().NotContain("class");
    }

    private static string Render(OwnerTypeModel owner, PolymorphicAttachmentModel attachment)
    {
        var template = new VirtualNavigationTemplate(owner, attachment);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static (OwnerTypeModel Owner, PolymorphicAttachmentModel Attachment) BuildModel()
    {
        var owner = new OwnerTypeModel
        {
            TypeName = "Invoice",
            FullTypeName = "global::Sales.Invoice",
            Namespace = "Sales",
            IdType = "System.Guid"
        };

        var attachment = new PolymorphicAttachmentModel
        {
            Namespace = "Sales",
            TypeName = "Document",
            FullTypeName = "global::Sales.Document",
            OwnerTypes = ImmutableArray.Create(owner)
        };

        return (owner, attachment);
    }
}
