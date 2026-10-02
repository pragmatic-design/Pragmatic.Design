using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class EntitySettersCascadeTests
{
    [Fact]
    public void CascadeSourceProperty_EmitsEntityPropertyChanged()
    {
        var model = BuildModel();
        var cascadeProps = ImmutableHashSet.Create("BaseRate");
        var source = Render(model, cascadeProps);

        source.Should().Contain("EntityPropertyChanged<global::Sales.RoomType>.Create(Id, nameof(BaseRate), oldValue, value)");
    }

    [Fact]
    public void CascadeSourceProperty_CapturesOldValue()
    {
        var model = BuildModel();
        var cascadeProps = ImmutableHashSet.Create("BaseRate");
        var source = Render(model, cascadeProps);

        source.Should().Contain("var oldValue = BaseRate;");
    }

    [Fact]
    public void CascadeSourceProperty_ImplementsIHasDomainEvents()
    {
        var model = BuildModel();
        var cascadeProps = ImmutableHashSet.Create("BaseRate");
        var source = Render(model, cascadeProps);

        source.Should().Contain("IHasDomainEvents");
        source.Should().Contain("_domainEvents");
        source.Should().Contain("DomainEvents");
        source.Should().Contain("ClearDomainEvents");
    }

    [Fact]
    public void NonCascadeProperty_DoesNotEmitEvent()
    {
        var model = BuildModel();
        var cascadeProps = ImmutableHashSet.Create("BaseRate");
        var source = Render(model, cascadeProps);

        // Name is not a cascade source, should not have event emission
        var nameSetterStart = source.IndexOf("SetName(");
        var nameSetterEnd = source.IndexOf("}", nameSetterStart + 100);
        var nameSection = source.Substring(nameSetterStart, nameSetterEnd - nameSetterStart);
        nameSection.Should().NotContain("EntityPropertyChanged");
        nameSection.Should().NotContain("oldValue");
    }

    [Fact]
    public void NoCascadeProperties_DoesNotImplementIHasDomainEvents()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().NotContain("IHasDomainEvents");
        source.Should().NotContain("_domainEvents");
    }

    private static string Render(EntityMetadataModel model, ImmutableHashSet<string>? cascadeProps = null)
    {
        var template = new EntitySettersTemplate(model, cascadeProps);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static EntityMetadataModel BuildModel()
    {
        return new EntityMetadataModel
        {
            TypeName = "RoomType",
            FullTypeName = "Sales.RoomType",
            Namespace = "Sales",
            IdType = "Guid",
            Accessibility = "public",
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "Name",
                    TypeName = "string",
                    HasPrivateSetter = true
                },
                new PropertyMetadataModel
                {
                    Name = "BaseRate",
                    TypeName = "decimal",
                    HasPrivateSetter = true
                })
        };
    }
}
