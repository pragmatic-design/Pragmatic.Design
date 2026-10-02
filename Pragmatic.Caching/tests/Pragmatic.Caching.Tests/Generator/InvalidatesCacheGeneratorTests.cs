using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Tests.Generator;

/// <summary>
///     Tests for [InvalidatesCache] attribute source generation.
/// </summary>
public class InvalidatesCacheGeneratorTests : CachingGeneratorTestBase
{
    [Fact]
    public async Task InvalidatesCache_ConventionBased_GeneratesHandler()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [InvalidatesCache]
                     public partial class UserUpdated
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty("Generated code should compile without errors");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task InvalidatesCache_ExplicitTags_GeneratesCorrectInvalidation()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [InvalidatesCache("users", "profiles")]
                     public partial class UserProfileChanged
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task InvalidatesCache_WithPlaceholders_ExpandsAtRuntime()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [InvalidatesCache("users", "tenant:{TenantId}")]
                     public partial class UserCreated
                     {
                         public int UserId { get; init; }
                         public int TenantId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task InvalidatesCache_WithKeys_GeneratesRemoveAsync()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [InvalidatesCache("users", Keys = new[] { "user:{UserId}" })]
                     public partial class UserDeleted
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task InvalidatesCache_EventSuffix_RemovesSuffixForConvention()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [InvalidatesCache]
                     public partial class OrderCreatedEvent
                     {
                         public int OrderId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        // Should generate "orders" tag from "OrderCreatedEvent"
        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task InvalidatesCache_OnRecord_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [InvalidatesCache("products")]
                     public partial record ProductPriceChanged(int ProductId, decimal NewPrice);
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }
}