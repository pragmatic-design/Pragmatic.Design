using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class LookupNavigationTemplateTests
{
    [Fact]
    public void RenderOutput_GeneratesNavigationProperty()
    {
        var consumer = BuildConsumer(nullable: false);
        var source = Render(consumer);

        source.Should().Contain("public partial class Property");
        source.Should().Contain("[NotMapped]");
        source.Should().Contain("LookupResolver.Get<");
    }

    [Fact]
    public void RenderOutput_NonNullableFk_GeneratesDirectGet()
    {
        var consumer = BuildConsumer(nullable: false);
        var source = Render(consumer);

        source.Should().Contain("global::Sales.Category Category =>");
        source.Should().Contain("LookupResolver.Get<global::Sales.Category, global::System.Guid>(CategoryId)");
        source.Should().NotContain("?");
    }

    [Fact]
    public void RenderOutput_NullableFk_GeneratesNullableProperty()
    {
        var consumer = BuildConsumer(nullable: true);
        var source = Render(consumer);

        source.Should().Contain("global::Sales.Category? Category =>");
        // A nullable FK resolves via TryGet so a non-null FK to an absent id yields null, not a throw.
        source.Should().Contain("CategoryId is {} __fk && global::Pragmatic.Persistence.Entity.LookupResolver.TryGet<");
        source.Should().Contain("(__fk, out var __v) ? __v : null");
        source.Should().NotContain("CategoryId is {} __fk ? global::Pragmatic.Persistence.Entity.LookupResolver.Get<");
    }

    [Fact]
    public void RenderOutput_HintNameIncludesConsumerAndLookup()
    {
        var consumer = BuildConsumer(nullable: false);
        var template = new LookupNavigationTemplate(consumer);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Contain("Property");
        artifact.HintName.Should().Contain("Category");
        artifact.HintName.Should().Contain("Navigation");
    }

    [Fact]
    public void Validate_EmptyTypeName_DoesNotRender()
    {
        var consumer = new LookupConsumerModel
        {
            Namespace = "Sales",
            TypeName = "",
            FullTypeName = "",
            FkPropertyName = "CategoryId",
            FkPropertyType = "System.Guid",
            Lookup = new LookupModel
            {
                Namespace = "Sales",
                TypeName = "Category",
                FullTypeName = "Sales.Category",
                IdType = "System.Guid"
            }
        };

        var template = new LookupNavigationTemplate(consumer);
        var artifact = template.RenderOutput();
        artifact.Text.Should().NotContain("class");
    }

    private static string Render(LookupConsumerModel consumer)
    {
        var template = new LookupNavigationTemplate(consumer);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static LookupConsumerModel BuildConsumer(bool nullable) => new()
    {
        Namespace = "Sales",
        TypeName = "Property",
        FullTypeName = "Sales.Property",
        FkPropertyName = "CategoryId",
        FkPropertyType = nullable ? "System.Guid?" : "System.Guid",
        IsFkNullable = nullable,
        Lookup = new LookupModel
        {
            Namespace = "Sales",
            TypeName = "Category",
            FullTypeName = "Sales.Category",
            IdType = "System.Guid"
        }
    };
}
