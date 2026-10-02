using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Caching.Models;
using Pragmatic.SourceGenerator.Features.Caching.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Caching;

public class InvalidatorTemplateTests
{
    [Fact]
    public void RenderOutput_SimpleModel_HintNameUsesVirtualFolder()
    {
        var model = BuildModel("MyApp.Orders", "PlaceOrderMutation");

        var artifact = new InvalidatorTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("MyApp.Orders.PlaceOrderMutation.CacheInvalidator.g.cs");
    }

    [Fact]
    public void RenderOutput_WithTags_GeneratesInvalidateByTagAsync()
    {
        var model = BuildModel("MyApp", "UpdateUserMutation", tags: ["users"]);

        var source = new InvalidatorTemplate(model).RenderOutput().Text;

        source.Should().Contain("InvalidateByTagAsync");
        source.Should().Contain("\"users\"");
    }

    [Fact]
    public void RenderOutput_WithKeys_GeneratesRemoveAsync()
    {
        var model = BuildModel("MyApp", "DeleteOrderMutation", keys: ["order:123"]);

        var source = new InvalidatorTemplate(model).RenderOutput().Text;

        source.Should().Contain("RemoveAsync");
        source.Should().Contain("\"order:123\"");
    }

    [Fact]
    public void RenderOutput_WithPlaceholderInTag_ExpandsToThisProperty()
    {
        var model = BuildModel("MyApp", "UpdateUserMutation",
            tags: ["{UserId}"],
            properties: [new PlaceholderPropertyModel { Name = "UserId", Type = "int" }]);

        var source = new InvalidatorTemplate(model).RenderOutput().Text;

        source.Should().Contain("{this.UserId}");
    }

    [Fact]
    public void RenderOutput_ImplementsICacheInvalidator()
    {
        var model = BuildModel("MyApp", "SomeMutation");

        var source = new InvalidatorTemplate(model).RenderOutput().Text;

        source.Should().Contain("ICacheInvalidator");
    }

    [Fact]
    public void RenderOutput_IsPartialClass()
    {
        var model = BuildModel("MyApp", "SomeMutation");

        var source = new InvalidatorTemplate(model).RenderOutput().Text;

        source.Should().Contain("partial class SomeMutation");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static InvalidatesModel BuildModel(
        string ns,
        string typeName,
        ImmutableArray<string>? tags = null,
        ImmutableArray<string>? keys = null,
        ImmutableArray<PlaceholderPropertyModel>? properties = null) => new()
        {
            Namespace = ns,
            TypeName = typeName,
            Accessibility = "public",
            TypeKind = "class",
            Tags = tags ?? ImmutableArray.Create("items"),
            Keys = keys ?? ImmutableArray<string>.Empty,
            Properties = properties ?? ImmutableArray<PlaceholderPropertyModel>.Empty
        };
}
