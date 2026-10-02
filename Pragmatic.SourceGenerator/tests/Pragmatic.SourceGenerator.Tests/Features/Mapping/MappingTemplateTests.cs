using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Mapping.Models;
using Pragmatic.SourceGenerator.Features.Mapping.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Mapping;

/// <summary>
/// Template unit tests — pure model → output, zero Roslyn compilation.
/// Verifies hint name convention and FromEntity body generation.
/// </summary>
public class MappingTemplateTests
{
    [Fact]
    public void RenderOutput_HintName_FollowsVirtualFolderConvention()
    {
        var model = BuildMapFromModel("MyApp.Catalog", "UserDto", "MyApp.Domain.User");

        var artifact = new MappingTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("MyApp.Catalog.UserDto.Mapping.g.cs");
    }

    [Fact]
    public void RenderOutput_NoNamespace_HintNameHasNoFolder()
    {
        var model = BuildMapFromModel(string.Empty, "PlainDto", "Domain.Source");

        var artifact = new MappingTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("PlainDto.Mapping.g.cs");
    }

    [Fact]
    public void RenderOutput_MapFrom_GeneratesFromEntityFactory()
    {
        var model = BuildMapFromModel("MyApp", "OrderDto", "MyApp.Domain.Order", props:
        [
            Prop("Code")
        ]);

        var source = new MappingTemplate(model).RenderOutput().Text;

        source.Should().Contain("public static OrderDto FromEntity(global::MyApp.Domain.Order entity)");
        // The context struct and result initializer both reference the resolved property.
        source.Should().Contain("Code = ctx.Code,");
    }

    [Fact]
    public void RenderOutput_UnresolvedProperty_NotEmittedInResult()
    {
        // Resolution.None properties are skipped in the FromEntity result initializer.
        var model = BuildMapFromModel("MyApp", "OrderDto", "MyApp.Domain.Order", props:
        [
            Prop("Code"),
            new() { PropertyName = "Unmapped", PropertyType = "string", Resolution = MappingResolution.None }
        ]);

        var source = new MappingTemplate(model).RenderOutput().Text;

        source.Should().Contain("Code = ctx.Code,");
        source.Should().NotContain("Unmapped = ctx.Unmapped,");
    }

    [Fact]
    public void RenderOutput_IgnoredProperty_NotEmittedInResult()
    {
        var model = BuildMapFromModel("MyApp", "OrderDto", "MyApp.Domain.Order", props:
        [
            Prop("Code"),
            new() { PropertyName = "Secret", PropertyType = "string", SourceExpression = "entity.Secret", Resolution = MappingResolution.DirectMatch, IsIgnored = true }
        ]);

        var source = new MappingTemplate(model).RenderOutput().Text;

        source.Should().Contain("Code = ctx.Code,");
        source.Should().NotContain("Secret = ctx.Secret,");
    }

    private static PropertyMappingModel Prop(string name) => new()
    {
        PropertyName = name,
        PropertyType = "string",
        SourceExpression = $"entity.{name}",
        Resolution = MappingResolution.DirectMatch
    };

    private static MappingModel BuildMapFromModel(
        string ns,
        string typeName,
        string sourceFullName,
        ImmutableArray<PropertyMappingModel>? props = null) => new()
        {
            Namespace = ns,
            TypeName = typeName,
            Accessibility = "public",
            TypeKind = "class",
            HasMapFrom = true,
            SourceTypeFullName = sourceFullName,
            SourceTypeName = sourceFullName.Split('.').Last(),
            Properties = props ?? ImmutableArray<PropertyMappingModel>.Empty
        };
}
