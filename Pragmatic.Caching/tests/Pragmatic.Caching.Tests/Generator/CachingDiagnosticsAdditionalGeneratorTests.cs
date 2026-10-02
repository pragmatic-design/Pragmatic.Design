using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Tests.Generator;

/// <summary>
///     Tests for the Caching SG diagnostics not covered by <see cref="CachingDiagnosticsGeneratorTests"/>:
///     PRAG1702 (no key properties), PRAG1750 (duplicate order), PRAG1751 (all properties excluded).
/// </summary>
public class CachingDiagnosticsAdditionalGeneratorTests : CachingGeneratorTestBase
{
    // --- PRAG1702: no properties to build a cache key ---

    [Fact]
    public void Cacheable_NoProperties_EmitsPRAG1702()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class GetSingleton
                     {
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1702").Should().BeTrue(
            "a [Cacheable] type with no properties cannot build a cache key");
    }

    [Fact]
    public void Cacheable_WithProperty_DoesNotEmitPRAG1702()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class GetUser
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1702").Should().BeFalse();
    }

    // --- PRAG1750: duplicate CacheKey Order ---

    [Fact]
    public void Cacheable_DuplicateCacheKeyOrder_EmitsPRAG1750()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class GetOrderLine
                     {
                         [CacheKey(Order = 1)]
                         public int OrderId { get; init; }

                         [CacheKey(Order = 1)]
                         public int LineId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1750").Should().BeTrue(
            "two properties sharing the same CacheKey Order should warn about non-deterministic ordering");
    }

    [Fact]
    public void Cacheable_DistinctCacheKeyOrder_DoesNotEmitPRAG1750()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class GetOrderLine
                     {
                         [CacheKey(Order = 1)]
                         public int OrderId { get; init; }

                         [CacheKey(Order = 2)]
                         public int LineId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1750").Should().BeFalse();
    }

    // --- PRAG1751: all properties excluded from the cache key ---

    [Fact]
    public void Cacheable_AllPropertiesExcluded_EmitsPRAG1751()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class GetFlag
                     {
                         [CacheKey(Exclude = true)]
                         public bool ForceRefresh { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1751").Should().BeTrue(
            "excluding every property from the cache key risks cache collisions and should warn");
    }

    [Fact]
    public void Cacheable_SomePropertiesIncluded_DoesNotEmitPRAG1751()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class GetFlag
                     {
                         public int Id { get; init; }

                         [CacheKey(Exclude = true)]
                         public bool ForceRefresh { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1751").Should().BeFalse();
    }
}
