using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for collection mapping in MappingSourceGenerator.
///     <para>
///         Matrix of combinations tested:
///         <list type="table">
///             <listheader>
///                 <term>Collection Type</term>
///                 <description>Element: Simple | DTO</description>
///             </listheader>
///             <item>
///                 <term>List&lt;T&gt;</term><description>Simple, DTO</description>
///             </item>
///             <item>
///                 <term>T[]</term><description>Simple, DTO</description>
///             </item>
///             <item>
///                 <term>HashSet&lt;T&gt;</term><description>Simple</description>
///             </item>
///             <item>
///                 <term>IEnumerable&lt;T&gt;</term><description>Simple</description>
///             </item>
///             <item>
///                 <term>Dictionary&lt;K,V&gt;</term><description>Simple value</description>
///             </item>
///         </list>
///     </para>
/// </summary>
public class CollectionMappingGeneratorTests : MappingGeneratorTestBase
{
    // =========================================================================
    // List<T> - Simple Element
    // =========================================================================

    [Fact]
    public async Task ListOfString_CopiesWithToList()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public List<string> Tags { get; set; } = new(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public List<string> Tags { get; init; } = new(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain(".ToList()");
        mainSource.Should().NotContain(".Select(");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task ListOfInt_CopiesWithToList()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public List<int> Numbers { get; set; } = new(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public List<int> Numbers { get; init; } = new(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain(".ToList()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // T[] - Simple Element
    // =========================================================================

    [Fact]
    public async Task ArrayOfString_CopiesWithToArray()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public string[] Tags { get; set; } = []; }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public string[] Tags { get; init; } = []; }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain(".ToArray()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task ArrayOfInt_CopiesWithToArray()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public int[] Values { get; set; } = []; }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public int[] Values { get; init; } = []; }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain(".ToArray()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // HashSet<T> - Simple Element
    // =========================================================================

    [Fact]
    public async Task HashSetOfString_CopiesWithToHashSet()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public HashSet<string> Tags { get; set; } = new(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public HashSet<string> Tags { get; init; } = new(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain(".ToHashSet()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // List<T> - DTO Element (requires mapping)
    // =========================================================================

    [Fact]
    public async Task ListOfDto_MapsEachElement()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Order { public int Id { get; set; } }
                         public class Customer { public List<Order> Orders { get; set; } = new(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Order>]
                         public partial record OrderDto { public int Id { get; init; } }

                         [MapFrom<Test.Entities.Customer>]
                         public partial record CustomerDto { public List<OrderDto> Orders { get; init; } = new(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "CustomerDto.Mapping");
        // Should use Select to map each element
        mainSource.Should().Contain(".Select(");
        mainSource.Should().Contain("OrderDto.FromEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // T[] - DTO Element (requires mapping)
    // =========================================================================

    [Fact]
    public async Task ArrayOfDto_MapsEachElement()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Item { public int Id { get; set; } }
                         public class Container { public Item[] Items { get; set; } = []; }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Item>]
                         public partial record ItemDto { public int Id { get; init; } }

                         [MapFrom<Test.Entities.Container>]
                         public partial record ContainerDto { public ItemDto[] Items { get; init; } = []; }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ContainerDto.Mapping");
        mainSource.Should().Contain(".Select(");
        mainSource.Should().Contain(".ToArray()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Dictionary<K,V> - Simple Value
    // =========================================================================

    [Fact]
    public async Task DictionaryStringInt_CopiesWithNewDictionary()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public Dictionary<string, int> Scores { get; set; } = new(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public Dictionary<string, int> Scores { get; init; } = new(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain("new Dictionary<");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P1: IEnumerable<T> Same Type
    // =========================================================================

    [Fact]
    public async Task IEnumerableOfInt_MapsDirectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public IEnumerable<int> Values { get; set; } = []; }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public IEnumerable<int> Values { get; init; } = []; }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        // IEnumerable<T> to IEnumerable<T> creates a copy with ToList for safety
        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain(".ToList()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P1: ICollection<T> Same Type
    // =========================================================================

    [Fact]
    public async Task ICollectionOfString_MapsToList()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public ICollection<string> Items { get; set; } = new List<string>(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public ICollection<string> Items { get; init; } = new List<string>(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain(".ToList()"); // ICollection needs concrete type

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P1: IList<T> Same Type
    // =========================================================================

    [Fact]
    public async Task IListOfInt_MapsToList()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public IList<int> Numbers { get; set; } = new List<int>(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public IList<int> Numbers { get; init; } = new List<int>(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain(".ToList()"); // IList needs concrete type

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P1: IReadOnlyCollection<T> Same Type
    // =========================================================================

    [Fact]
    public async Task IReadOnlyCollectionOfString_MapsToList()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public IReadOnlyCollection<string> Tags { get; set; } = []; }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public IReadOnlyCollection<string> Tags { get; init; } = []; }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain(".ToList()"); // IReadOnlyCollection needs concrete type

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P1: HashSet<T> -> List<T> Conversion
    // =========================================================================

    [Fact]
    public async Task HashSetToList_Conversion()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public HashSet<string> Tags { get; set; } = new(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public List<string> Tags { get; init; } = new(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain(".ToList()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P1: IEnumerable<Entity> -> List<Dto> Conversion
    // =========================================================================

    [Fact]
    public async Task IEnumerableOfEntityToListOfDto_MapsEachElement()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Item { public int Id { get; set; } }
                         public class Container { public IEnumerable<Item> Items { get; set; } = []; }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Item>]
                         public partial record ItemDto { public int Id { get; init; } }

                         [MapFrom<Test.Entities.Container>]
                         public partial record ContainerDto { public List<ItemDto> Items { get; init; } = new(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ContainerDto.Mapping");
        mainSource.Should().Contain(".Select(");
        mainSource.Should().Contain("ItemDto.FromEntity");
        mainSource.Should().Contain(".ToList()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P1: List<Entity> -> Dto[] Conversion
    // =========================================================================

    [Fact]
    public async Task ListOfEntityToArrayOfDto_MapsEachElement()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Product { public int Id { get; set; } public string Name { get; set; } = ""; }
                         public class Catalog { public List<Product> Products { get; set; } = new(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Product>]
                         public partial record ProductDto { public int Id { get; init; } public string Name { get; init; } = ""; }

                         [MapFrom<Test.Entities.Catalog>]
                         public partial record CatalogDto { public ProductDto[] Products { get; init; } = []; }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "CatalogDto.Mapping");
        mainSource.Should().Contain(".Select(");
        mainSource.Should().Contain("ProductDto.FromEntity");
        mainSource.Should().Contain(".ToArray()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P1: Nullable Array T[]?
    // =========================================================================

    [Fact]
    public async Task NullableArrayOfString_HandlesNullSafely()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public string[]? Tags { get; set; } }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public string[]? Tags { get; init; } }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        // Should have null-conditional
        mainSource.Should().Contain("?.ToArray()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P1: Dictionary Variants
    // =========================================================================

    [Fact]
    public async Task DictionaryIntString_MapsDirectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public Dictionary<int, string> Lookup { get; set; } = new(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public Dictionary<int, string> Lookup { get; init; } = new(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain("new Dictionary<");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task IDictionaryToDictionary_Conversion()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public IDictionary<string, int> Scores { get; set; } = new Dictionary<string, int>(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public Dictionary<string, int> Scores { get; init; } = new(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        // Uses copy constructor: new Dictionary<K,V>(source)
        mainSource.Should().Contain("new Dictionary<string, int>(");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task IReadOnlyDictionaryToDictionary_Conversion()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public IReadOnlyDictionary<string, int> Data { get; set; } = new Dictionary<string, int>(); }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public Dictionary<string, int> Data { get; init; } = new(); }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        // Uses copy constructor: new Dictionary<K,V>(source)
        mainSource.Should().Contain("new Dictionary<string, int>(");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task NullableDictionary_HandlesNullSafely()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public Dictionary<string, int>? Metadata { get; set; } }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public Dictionary<string, int>? Metadata { get; init; } }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        // Should have null-conditional
        mainSource.Should().Contain("?");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Nullable Collections
    // =========================================================================

    [Fact]
    public async Task NullableListOfString_HandlesNullSafely()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity { public List<string>? Tags { get; set; } }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto { public List<string>? Tags { get; init; } }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        // Should have null-conditional
        mainSource.Should().Contain("?.ToList()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Immutable collections (ImmutableArray / ImmutableList)
    // =========================================================================

    [Fact]
    public void MapFrom_ImmutableCollections_MaterializedCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using System.Collections.Immutable;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public List<string> Tags { get; set; } = [];
                             public List<int> Scores { get; set; } = [];
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public ImmutableArray<string> Tags { get; init; }
                             public ImmutableList<int> Scores { get; init; } = ImmutableList<int>.Empty;
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().Contain(".ToImmutableArray()")
            .And.Contain(".ToImmutableList()");
    }

    // =========================================================================
    // Write path honors the ENTITY's collection kind
    // =========================================================================

    [Fact]
    public void MapTo_EntityHashSet_MaterializesToHashSet()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Child { public string Name { get; set; } = ""; }
                         public class Entity
                         {
                             public HashSet<Child> Children { get; set; } = [];
                         }

                         [MapTo<Child>]
                         public partial record ChildDto { public string Name { get; init; } = ""; }

                         [MapTo<Entity>]
                         public partial record EntityDto
                         {
                             // DTO declares List, but the ENTITY declares HashSet → ToHashSet
                             public List<ChildDto> Children { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "EntityDto.Mapping");
        mainSource.Should().Contain(".ToHashSet()", "the write path must materialize what the entity declares");
    }
}