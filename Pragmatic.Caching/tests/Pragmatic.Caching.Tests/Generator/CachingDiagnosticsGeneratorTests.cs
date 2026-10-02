using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Tests.Generator;

/// <summary>
///     Tests for Caching SG diagnostic emission (PRAG1700-PRAG1799).
/// </summary>
public class CachingDiagnosticsGeneratorTests : CachingGeneratorTestBase
{
    // --- PRAG1700: [Cacheable] on non-partial class ---

    /// <summary>
    ///     PRAG1700 is the companion analyzer's, reported on the declaration; the generator skips the
    ///     type without a second copy.
    /// </summary>
    [Fact]
    public void Cacheable_NonPartialClass_IsSkippedWithoutAGeneratorDiagnostic()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public class GetUser
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1700").Should().BeFalse(
            "one ID, one owner: the analyzer reports it");
        HasCompilationErrors(result).Should().BeFalse("nothing was generated into a type that cannot take it");
    }

    [Fact]
    public void Cacheable_PartialClass_DoesNotEmitPRAG1700()
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

        HasDiagnostic(result, "PRAG1700").Should().BeFalse();
    }

    // --- PRAG1701: Invalid duration format ---

    [Fact]
    public void Cacheable_InvalidDuration_EmitsPRAG1701()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "abc")]
                     public partial class GetUser
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1701").Should().BeTrue(
            "invalid duration 'abc' should emit PRAG1701");
    }

    [Fact]
    public void Cacheable_ValidDurationMinutes_DoesNotEmitPRAG1701()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "10m")]
                     public partial class GetUser
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1701").Should().BeFalse();
    }

    [Fact]
    public void Cacheable_ValidDurationHours_DoesNotEmitPRAG1701()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "2h")]
                     public partial class GetUser
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1701").Should().BeFalse();
    }

    // --- PRAG1704: [InvalidatesCache] on non-partial class ---

    [Fact]
    public void InvalidatesCache_NonPartialClass_EmitsPRAG1704()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [InvalidatesCache("users")]
                     public class UserUpdated
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1704").Should().BeTrue(
            "non-partial class with [InvalidatesCache] should emit PRAG1704");
    }

    [Fact]
    public void InvalidatesCache_PartialClass_DoesNotEmitPRAG1704()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [InvalidatesCache("users")]
                     public partial class UserUpdated
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1704").Should().BeFalse();
    }

    // --- PRAG1703: Invalid tag placeholder ---

    [Fact]
    public void Cacheable_TagPlaceholder_NonExistentProperty_EmitsPRAG1703()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m", Tags = new[] { "tenant:{TenantId}" })]
                     public partial class GetUser
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1703").Should().BeTrue(
            "placeholder {TenantId} refers to non-existent property and should emit PRAG1703");
    }

    [Fact]
    public void Cacheable_TagPlaceholder_ExistingProperty_DoesNotEmitPRAG1703()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m", Tags = new[] { "tenant:{TenantId}" })]
                     public partial class GetUser
                     {
                         public int UserId { get; init; }
                         public int TenantId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1703").Should().BeFalse();
    }
}
