using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class EntityCreateTemplateTests
{
    [Fact]
    public void RenderOutput_WithDefaultValue_SetsPropertyInCreate()
    {
        var model = BuildModel("Sales", "Order",
            new PropertyMetadataModel
            {
                Name = "Currency",
                TypeName = "string",
                DefaultValueExpression = "\"EUR\"",
                HasDefaultValue = true,
                IsRequiredForCreate = false
            });

        var source = Render(model);

        source.Should().Contain("Currency = \"EUR\",");
    }

    [Fact]
    public void RenderOutput_WithIntDefaultValue_SetsPropertyInCreate()
    {
        var model = BuildModel("Sales", "Order",
            new PropertyMetadataModel
            {
                Name = "GuestCount",
                TypeName = "int",
                DefaultValueExpression = "1",
                HasDefaultValue = true,
                IsRequiredForCreate = false
            });

        var source = Render(model);

        source.Should().Contain("GuestCount = 1,");
    }

    [Fact]
    public void RenderOutput_DefaultValueProperty_NotInParameters()
    {
        var model = BuildModel("Sales", "Order",
            new PropertyMetadataModel
            {
                Name = "Currency",
                TypeName = "string",
                DefaultValueExpression = "\"EUR\"",
                HasDefaultValue = true,
                IsRequiredForCreate = false
            });

        var source = Render(model);

        // Currency should NOT be a method parameter since it has a default
        source.Should().NotContain("string currency");
    }

    [Fact]
    public void RenderOutput_RequiredProperty_IsInParameters()
    {
        var model = BuildModel("Sales", "Order",
            new PropertyMetadataModel
            {
                Name = "CustomerName",
                TypeName = "string",
                IsRequiredForCreate = true
            });

        var source = Render(model);

        source.Should().Contain("string customerName");
        source.Should().Contain("CustomerName = customerName,");
    }

    [Fact]
    public void RenderOutput_ComputedDefault_NotInCreate()
    {
        // ComputedDefault properties require async + DI, so should NOT be set in Create()
        var model = BuildModel("Sales", "Invoice",
            new PropertyMetadataModel
            {
                Name = "InvoiceNumber",
                TypeName = "string",
                ComputedDefaultGeneratorFqn = "Sales.InvoiceNumberGenerator",
                HasDefaultValue = true,
                IsRequiredForCreate = false
            });

        var source = Render(model);

        // Should NOT contain InvoiceNumber in Create body (it's computed async later)
        source.Should().NotContain("InvoiceNumber =");
        source.Should().NotContain("InvoiceNumberGenerator");
    }

    [Fact]
    public void RenderOutput_MixedProperties_HandlesAllCorrectly()
    {
        var model = BuildModel("Sales", "Order",
            new PropertyMetadataModel
            {
                Name = "CustomerName",
                TypeName = "string",
                IsRequiredForCreate = true
            },
            new PropertyMetadataModel
            {
                Name = "Currency",
                TypeName = "string",
                DefaultValueExpression = "\"EUR\"",
                HasDefaultValue = true,
                IsRequiredForCreate = false
            },
            new PropertyMetadataModel
            {
                Name = "Notes",
                TypeName = "string?",
                IsNullable = true,
                IsRequiredForCreate = false
            });

        var source = Render(model);

        source.Should().Contain("string customerName");
        source.Should().Contain("CustomerName = customerName,");
        source.Should().Contain("Currency = \"EUR\",");
        source.Should().NotContain("string notes");
    }

    [Fact]
    public void RenderOutput_NoRequiredProps_ImplementsICreatable()
    {
        var model = BuildModel("Sales", "Order");

        var source = Render(model);

        source.Should().Contain("ICreatable<Order>");
    }

    /// <summary>
    ///     ⚠️ Even with required properties the factory has an empty overload, and the entity implements
    ///     <c>ICreatable</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The reason is uniformity: whoever builds — a mutation's invoker, <c>ToEntity</c> — does not
    ///         see the factory and would have to <em>predict</em> its signature, that is keep a second copy
    ///         of the rule «what is required at creation». An overload that is always there leaves nothing
    ///         to predict.
    ///     </para>
    ///     <para>
    ///         The empty overload does not make an incomplete entity valid: the required properties stay
    ///         at their default until the caller writes them, exactly as <c>new</c> would leave them. What
    ///         it adds over <c>new</c> is the rest — key, <c>[DefaultValue]</c>,
    ///         <c>[ComputedDefault]</c>, audit — which <c>new</c> skips.
    ///     </para>
    /// </remarks>
    [Fact]
    public void RenderOutput_WithRequiredProps_AlsoEmitsAParameterlessOverload()
    {
        var model = BuildModel("Sales", "Order",
            new PropertyMetadataModel
            {
                Name = "CustomerName",
                TypeName = "string",
                IsRequiredForCreate = true
            });

        var source = Render(model);

        source.Should().Contain("Create(string customerName",
            "the overload with the required members stays: whoever has those values passes them");
        source.Should().Contain("Create()",
            "and next to it the empty one, which is what makes construction uniform across entities");
        source.Should().Contain("ICreatable<Order>",
            "with a Create() always present the contract holds for every entity, not only for the few "
            + "whose factory happens to be empty");
    }

    [Fact]
    public void RenderOutput_AuditableNoRequiredProps_ImplementsICreatableWithExplicit()
    {
        var model = BuildModel("Sales", "Order") with { IsAuditable = true };

        var source = Render(model);

        source.Should().Contain("ICreatable<Order>");
        // Explicit interface implementation for parameterless Create()
        source.Should().Contain("ICreatable<Order>.Create()");
    }

    private static string Render(EntityMetadataModel model)
    {
        var template = new EntityCreateTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static EntityMetadataModel BuildModel(string ns, string name,
        params PropertyMetadataModel[] properties)
    {
        return new EntityMetadataModel
        {
            TypeName = name,
            FullTypeName = $"{ns}.{name}",
            Namespace = ns,
            IdType = "Guid",
            IsValid = true,
            Properties = properties.ToImmutableArray()
        };
    }
}
