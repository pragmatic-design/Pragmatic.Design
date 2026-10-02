using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Tests.Generator;

/// <summary>
///     Tests for category-routed generation:
///     <c>[Cacheable(Category = typeof(...))]</c> and <c>[InvalidatesCache(Keys = ..., Category = typeof(...))]</c>.
/// </summary>
public class CacheCategoryRoutingGeneratorTests : CachingGeneratorTestBase
{
    [Fact]
    public void Cacheable_WithCategory_GeneratesCacheCategoryProperty()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     public sealed class PermissionsCategory;

                     [Cacheable(Duration = "5m", Category = typeof(PermissionsCategory))]
                     public partial class GetPermissions
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        GetCompilationErrors(result).Should().BeEmpty();

        var generated = GetGeneratedSourcesAsDictionary(result);
        var cacheable = generated.Values.FirstOrDefault(s => s.Contains("class GetPermissions"));
        cacheable.Should().NotBeNull("a partial for GetPermissions should be generated");
        cacheable.Should().Contain("CacheCategory");
        cacheable.Should().Contain("typeof(global::TestNamespace.PermissionsCategory)");
    }

    [Fact]
    public void Cacheable_WithoutCategory_DoesNotGenerateCacheCategoryProperty()
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

        GetCompilationErrors(result).Should().BeEmpty();

        var generated = GetGeneratedSourcesAsDictionary(result);
        var cacheable = generated.Values.FirstOrDefault(s => s.Contains("class GetUser"));
        cacheable.Should().NotBeNull();
        // Without a Category, the SG relies on the ICacheable default (null) and emits no override.
        cacheable.Should().NotContain("CacheCategory");
    }

    [Fact]
    public void InvalidatesCache_WithKeysAndCategory_GeneratesRemoveAndCategory()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     public sealed class UsersCategory;

                     [InvalidatesCache("users", Keys = new[] { "user:{UserId}" }, Category = typeof(UsersCategory))]
                     public partial class UserDeleted
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        GetCompilationErrors(result).Should().BeEmpty();

        var generated = GetGeneratedSourcesAsDictionary(result);
        var invalidator = generated.Values.FirstOrDefault(s => s.Contains("class UserDeleted"));
        invalidator.Should().NotBeNull("an invalidator partial for UserDeleted should be generated");

        // Explicit Keys produce RemoveAsync calls.
        invalidator.Should().Contain("RemoveAsync");
        // Tag still produces InvalidateByTagAsync.
        invalidator.Should().Contain("InvalidateByTagAsync");
        // Category routing surfaces an InvalidationCategory of the marker type.
        invalidator.Should().Contain("InvalidationCategory");
        invalidator.Should().Contain("typeof(global::TestNamespace.UsersCategory)");
    }

    [Fact]
    public void InvalidatesCache_ConventionBased_EmptyTags_DerivesTagFromName()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [InvalidatesCache]
                     public partial class CustomerUpdated
                     {
                         public int CustomerId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        GetCompilationErrors(result).Should().BeEmpty();

        var generated = GetGeneratedSourcesAsDictionary(result);
        var invalidator = generated.Values.FirstOrDefault(s => s.Contains("class CustomerUpdated"));
        invalidator.Should().NotBeNull();
        // Convention: empty Tags derives a tag from the event name (CustomerUpdated -> customers).
        invalidator.Should().Contain("InvalidateByTagAsync");
        invalidator.Should().Contain("customers");
    }
}
