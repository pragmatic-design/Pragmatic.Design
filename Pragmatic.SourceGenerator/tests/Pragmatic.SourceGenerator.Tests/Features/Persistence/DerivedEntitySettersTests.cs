using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class DerivedEntitySettersTests
{
    [Fact]
    public void DerivedType_WithPrivateSetters_GeneratesSetMethods()
    {
        var model = BuildDerivedModel(
            new PropertyMetadataModel
            {
                Name = "CardNumber",
                TypeName = "string",
                HasPrivateSetter = true
            },
            new PropertyMetadataModel
            {
                Name = "ExpiryDate",
                TypeName = "string",
                HasPrivateSetter = true
            });

        var source = Render(model);

        source.Should().Contain("SetCardNumber(");
        source.Should().Contain("CardNumber = value;");
        source.Should().Contain("SetExpiryDate(");
        source.Should().Contain("ExpiryDate = value;");
    }

    [Fact]
    public void DerivedType_WithPublicSetters_NoGeneration()
    {
        var model = BuildDerivedModel(
            new PropertyMetadataModel
            {
                Name = "CardNumber",
                TypeName = "string",
                HasPrivateSetter = false
            },
            new PropertyMetadataModel
            {
                Name = "ExpiryDate",
                TypeName = "string",
                HasPrivateSetter = false
            });

        // Validate returns false when no private setter properties exist
        model.HasPrivateSetterProperties.Should().BeFalse();

        var template = new DerivedEntitySettersTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void DerivedType_DoesNotGenerateChangeTracking()
    {
        var model = BuildDerivedModel(
            new PropertyMetadataModel
            {
                Name = "CardNumber",
                TypeName = "string",
                HasPrivateSetter = true
            });

        var source = Render(model);

        // Derived types should NOT have IChangeTracking infrastructure
        // (that's on the base entity's generated partial)
        source.Should().NotContain("IChangeTracking");
        source.Should().NotContain("_modifiedProperties");
        source.Should().NotContain("ModifiedProperties");
        source.Should().NotContain("ResetModifiedProperties");
        source.Should().NotContain("_collectionsModified");
    }

    [Fact]
    public void DerivedType_GeneratesCorrectNamespace()
    {
        var model = BuildDerivedModel(
            new PropertyMetadataModel
            {
                Name = "CardNumber",
                TypeName = "string",
                HasPrivateSetter = true
            });

        var source = Render(model);

        source.Should().Contain("namespace Sales;");
        source.Should().Contain("partial class CreditCardPayment");
    }

    [Fact]
    public void DerivedType_MixedSetters_GeneratesOnlyForPrivate()
    {
        var model = BuildDerivedModel(
            new PropertyMetadataModel
            {
                Name = "CardNumber",
                TypeName = "string",
                HasPrivateSetter = true
            },
            new PropertyMetadataModel
            {
                Name = "Notes",
                TypeName = "string",
                HasPrivateSetter = false
            });

        var source = Render(model);

        source.Should().Contain("SetCardNumber(");
        source.Should().NotContain("SetNotes(");
    }

    private static string Render(EntityMetadataModel model)
    {
        var template = new DerivedEntitySettersTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static EntityMetadataModel BuildDerivedModel(params PropertyMetadataModel[] properties)
    {
        return new EntityMetadataModel
        {
            TypeName = "CreditCardPayment",
            FullTypeName = "Sales.CreditCardPayment",
            Namespace = "Sales",
            IdType = "Guid",
            IsValid = true,
            Accessibility = "public",
            Properties = properties.ToImmutableArray()
        };
    }
}
