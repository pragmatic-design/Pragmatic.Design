using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Resource.Models;
using Pragmatic.SourceGenerator.Features.Resource.Transforms;
using Pragmatic.SourceGenerator.Tests.Core;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resource;

/// <summary>
///     <c>ResourceCrudTransform</c> keeps only scalar properties for the generated CRUD DTOs. Its
///     collection filter was a seven-name whitelist, so <c>Dictionary&lt;,&gt;</c>, <c>T[]</c> and
///     <c>ImmutableArray&lt;T&gt;</c> passed straight through and were emitted into the DTOs as if they
///     were scalars.
/// </summary>
public class ResourceCrudCollectionShapeTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;

        namespace MyApp.Catalog
        {
            public sealed class Photo { }

            public sealed class Product
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = "";
                public decimal Price { get; set; }

                public List<Photo> Gallery { get; set; } = null!;
                public HashSet<string> Keywords { get; set; } = null!;
                public Dictionary<string, string> Attributes { get; set; } = null!;
                public string[] Aliases { get; set; } = null!;
                public ImmutableArray<int> Ratings { get; set; }
            }
        }
        """;

    private static ResourceCrudModel Build()
    {
        var compilation = SymbolCompilationHelper.Compile(Source);
        var model = ResourceCrudTransform.Build(
            new ResourceModel
            {
                Namespace = "MyApp.Catalog",
                TypeName = "Product",
                FullTypeName = "MyApp.Catalog.Product",
                Segment = "products",
                IdType = "System.Guid"
            },
            compilation);

        model.Should().NotBeNull();
        return model!;
    }

    [Theory]
    [InlineData("Gallery")]     // already excluded
    [InlineData("Keywords")]    // HashSet<T> — leaked into the DTOs
    [InlineData("Attributes")]  // Dictionary<,> — leaked into the DTOs
    [InlineData("Aliases")]     // T[] — leaked into the DTOs
    [InlineData("Ratings")]     // ImmutableArray<T> — leaked into the DTOs
    public void Build_CollectionProperty_IsNotAScalarDtoProperty(string propertyName)
        => Build().Properties.Select(p => p.Name).Should().NotContain(propertyName);

    [Fact]
    public void Build_ScalarProperties_AreKept()
        => Build().Properties.Select(p => p.Name)
            .Should().Contain(["Id", "Name", "Price"]);
}
