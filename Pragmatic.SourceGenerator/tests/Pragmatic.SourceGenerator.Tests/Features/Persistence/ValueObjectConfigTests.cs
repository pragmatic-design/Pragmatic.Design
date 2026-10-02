using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     An entity property typed as a [ValueObject] is mapped as an EF
///     Core complex type (builder.ComplexProperty), generalizing the hardcoded Money handling. The same
///     {Property}_{Sub} columns are emitted in SchemaMetadata (covered by SchemaMetadata tests).
/// </summary>
public class ValueObjectConfigTests
{
    private static EntityMetadataModel BuildModel(params ValueObjectColumnModel[] voColumns)
        => new()
        {
            TypeName = "Customer",
            FullTypeName = "Sales.Customer",
            Namespace = "Sales",
            IdType = "Guid",
            IsValid = true,
            Properties = new[]
            {
                new PropertyMetadataModel
                {
                    Name = "Address",
                    TypeName = "Sales.Address",
                    HasPrivateSetter = true,
                    ValueObjectColumns = voColumns.ToEquatableArray()
                }
            }.ToEquatableArray()
        };

    [Fact]
    public void ValueObjectProperty_EmitsComplexProperty()
    {
        var model = BuildModel(
            new ValueObjectColumnModel { ColumnName = "Address_Street", TypeName = "string" },
            new ValueObjectColumnModel { ColumnName = "Address_City", TypeName = "string" });

        var source = new EntityConfigurationTemplate(model).RenderOutput().Text;

        source.Should().Contain("builder.ComplexProperty(e => e.Address);");
        // Not configured as a scalar property — that would break complex-type mapping.
        source.Should().NotContain("builder.Property(e => e.Address)");
    }

    [Fact]
    public void NonValueObjectProperty_EmitsScalarProperty()
    {
        // No ValueObjectColumns → ordinary scalar property configuration, not ComplexProperty.
        var model = BuildModel();

        var source = new EntityConfigurationTemplate(model).RenderOutput().Text;

        source.Should().Contain("builder.Property(e => e.Address)");
        source.Should().NotContain("ComplexProperty(e => e.Address)");
    }
}
