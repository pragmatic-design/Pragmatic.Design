using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class InheritanceMappingTemplateTests
{
    [Fact]
    public void RenderOutput_Tph_GeneratesDiscriminator()
    {
        var model = BuildModel("Tph");
        var source = Render(model);

        source.Should().Contain("HasDiscriminator<string>(\"Discriminator\")");
        source.Should().Contain("HasValue<global::Sales.CreditCardPayment>(\"CreditCardPayment\")");
        source.Should().Contain("HasValue<global::Sales.BankTransferPayment>(\"BankTransferPayment\")");
    }

    [Fact]
    public void RenderOutput_Tpt_GeneratesToTable()
    {
        var model = BuildModel("Tpt");
        var source = Render(model);

        source.Should().Contain("ToTable(\"CreditCardPayments\")");
        source.Should().Contain("ToTable(\"BankTransferPayments\")");
    }

    [Fact]
    public void RenderOutput_Tpc_GeneratesUseTpcAndToTable()
    {
        var model = BuildModel("Tpc");
        var source = Render(model);

        source.Should().Contain("UseTpcMappingStrategy()");
        source.Should().Contain("ToTable(\"CreditCardPayments\")");
    }

    [Fact]
    public void RenderOutput_Tph_CustomDiscriminatorValue()
    {
        var model = new InheritanceMappingModel
        {
            Namespace = "Sales",
            BaseTypeName = "Payment",
            BaseFullTypeName = "global::Sales.Payment",
            Strategy = "Tph",
            DerivedTypes = ImmutableArray.Create(
                new DerivedTypeModel
                {
                    TypeName = "CreditCardPayment",
                    FullTypeName = "global::Sales.CreditCardPayment",
                    DiscriminatorValue = "CC"
                })
        };

        var source = Render(model);
        source.Should().Contain("HasValue<global::Sales.CreditCardPayment>(\"CC\")");
    }

    [Fact]
    public void Validate_NoDerivedTypes_ReturnsEmpty()
    {
        var model = new InheritanceMappingModel
        {
            Namespace = "Sales",
            BaseTypeName = "Payment",
            BaseFullTypeName = "global::Sales.Payment",
            Strategy = "Tph"
        };

        var template = new InheritanceMappingTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    private static string Render(InheritanceMappingModel model)
    {
        var template = new InheritanceMappingTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static InheritanceMappingModel BuildModel(string strategy)
    {
        return new InheritanceMappingModel
        {
            Namespace = "Sales",
            BaseTypeName = "Payment",
            BaseFullTypeName = "global::Sales.Payment",
            Strategy = strategy,
            DerivedTypes = ImmutableArray.Create(
                new DerivedTypeModel
                {
                    TypeName = "CreditCardPayment",
                    FullTypeName = "global::Sales.CreditCardPayment"
                },
                new DerivedTypeModel
                {
                    TypeName = "BankTransferPayment",
                    FullTypeName = "global::Sales.BankTransferPayment"
                })
        };
    }
}
