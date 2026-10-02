using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Core;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     Collection detection is one shape-based rule shared by soft-delete cascade and Resource DTO
///     extraction. A list of BCL names would silently treat everything unlisted as a scalar. These
///     tests pin the rule.
/// </summary>
public class CollectionTypeHelperTests
{
    private const string Source = """
        using System.Collections.Generic;
        using System.Collections.Immutable;

        namespace MyApp
        {
            public sealed class Line { }

            // A user-defined collection: named nothing like a BCL type, still a collection.
            public sealed class LineBag : List<Line> { }

            public sealed class Holder
            {
                public List<Line> AsList { get; set; } = null!;
                public ICollection<Line> AsICollection { get; set; } = null!;
                public IEnumerable<Line> AsIEnumerable { get; set; } = null!;
                public HashSet<Line> AsHashSet { get; set; } = null!;
                public IReadOnlySet<Line> AsIReadOnlySet { get; set; } = null!;
                public Dictionary<string, Line> AsDictionary { get; set; } = null!;
                public ImmutableArray<Line> AsImmutableArray { get; set; }
                public Line[] AsArray { get; set; } = null!;
                public LineBag AsCustom { get; set; } = null!;

                public string Text { get; set; } = "";
                public int Number { get; set; }
                public Line Single { get; set; } = null!;
            }
        }
        """;

    private static bool IsCollection(string propertyName)
        => CollectionTypeHelper.IsCollection(
            SymbolCompilationHelper.GetPropertyType(Source, "MyApp.Holder", propertyName));

    [Theory]
    [InlineData("AsList")]
    [InlineData("AsICollection")]
    [InlineData("AsIEnumerable")]
    [InlineData("AsHashSet")]         // was unrecognised
    [InlineData("AsIReadOnlySet")]    // was unrecognised
    [InlineData("AsDictionary")]      // was unrecognised
    [InlineData("AsImmutableArray")]  // was unrecognised (and is a struct)
    [InlineData("AsArray")]           // was unrecognised
    [InlineData("AsCustom")]          // was unrecognised
    public void IsCollection_CollectionShapes_ReturnsTrue(string propertyName)
        => IsCollection(propertyName).Should().BeTrue();

    [Theory]
    [InlineData("Text")]  // string is IEnumerable<char> but is always a scalar here
    [InlineData("Number")]
    [InlineData("Single")]
    public void IsCollection_ScalarShapes_ReturnsFalse(string propertyName)
        => IsCollection(propertyName).Should().BeFalse();

    [Fact]
    public void GetElementTypes_Dictionary_YieldsTheValueType()
    {
        var elements = CollectionTypeHelper.GetElementTypes(
            SymbolCompilationHelper.GetPropertyType(Source, "MyApp.Holder", "AsDictionary"));

        elements.Select(e => e.Name).Should().Contain("Line",
            "a Dictionary<TKey,TEntity> navigation is a collection of TEntity to every caller that matters");
    }

    [Fact]
    public void GetElementTypes_Array_YieldsTheElementType()
    {
        var elements = CollectionTypeHelper.GetElementTypes(
            SymbolCompilationHelper.GetPropertyType(Source, "MyApp.Holder", "AsArray"));

        elements.Should().ContainSingle().Which.Name.Should().Be("Line");
    }
}
