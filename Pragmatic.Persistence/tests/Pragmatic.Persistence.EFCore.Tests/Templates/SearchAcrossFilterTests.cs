using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for [SearchAcross] support in GridFilterApplyTemplate.
/// </summary>
public class SearchAcrossFilterTests
{
    [Fact]
    public void SearchAcross_SingleProperty_GeneratesContains()
    {
        var model = CreateModelWithSearchAcross("Name");

        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("e.Name.Contains");
    }

    [Fact]
    public void SearchAcross_MultipleProperties_GeneratesOrChain()
    {
        var model = CreateModelWithSearchAcross("FirstName", "LastName", "Email");

        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("e.FirstName.Contains");
        source.Should().Contain("||");
        source.Should().Contain("e.LastName.Contains");
        source.Should().Contain("e.Email.Contains");
    }

    [Fact]
    public void SearchAcross_NullableProperty_GeneratesNullCheck()
    {
        var model = CreateModelWithSearchAcross(nullable: true, "Name", "Description");

        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("is not null");
        source.Should().Contain("e.Name.Contains");
        source.Should().Contain("e.Description.Contains");
    }

    [Fact]
    public void SearchAcross_CoexistsWithRegularFilters()
    {
        var model = new GridFilterModel
        {
            Namespace = "TestApp",
            TypeName = "GuestFilter",
            Accessibility = "public",
            TypeKind = "class",
            EntityTypeFullName = "TestApp.Guest",
            EntityTypeName = "Guest",
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Search",
                    PropertyType = "string",
                    IsNullable = true,
                    IsFilterable = true,
                    IsSearchAcross = true,
                    SearchAcrossPaths = ImmutableArray.Create("FirstName", "LastName", "Email")
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "int",
                    IsFilterable = true
                }
            )
        };

        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // SearchAcross generates OR chain
        source.Should().Contain("e.FirstName.Contains");
        source.Should().Contain("||");

        // Regular filter generates equality check
        source.Should().Contain("e.Status ==");
    }

    private static GridFilterModel CreateModelWithSearchAcross(params string[] paths)
        => CreateModelWithSearchAcross(nullable: true, paths);

    private static GridFilterModel CreateModelWithSearchAcross(bool nullable, params string[] paths)
    {
        return new GridFilterModel
        {
            Namespace = "TestApp",
            TypeName = "TestFilter",
            Accessibility = "public",
            TypeKind = "class",
            EntityTypeFullName = "TestApp.TestEntity",
            EntityTypeName = "TestEntity",
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Search",
                    PropertyType = "string",
                    IsNullable = nullable,
                    IsFilterable = true,
                    IsSearchAcross = true,
                    SearchAcrossPaths = paths.ToImmutableArray()
                }
            )
        };
    }
}
