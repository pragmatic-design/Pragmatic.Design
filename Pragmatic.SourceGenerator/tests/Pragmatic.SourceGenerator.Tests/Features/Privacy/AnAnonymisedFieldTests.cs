using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     <c>Anonymize</c> writes a value that identifies nobody <em>and that the column accepts</em>.
/// </summary>
/// <remarks>
///     The plan wrote <c>default!</c> for every anonymised field: null into a non-nullable string, which
///     compiles and fails at <c>SaveChanges</c> against the NOT NULL column — the erasure request fails
///     while it is being served. PRAG2909 refuses exactly that write for <c>Null</c>, and recommends
///     <c>Anonymize</c> as the way out.
/// </remarks>
public class AnAnonymisedFieldTests
{
    private static SourceGenRunResult Run(string members) =>
        GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(PrivacyTestSources.Stubs + $$"""

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Id { get; set; } = "";

            {{members}}
                }
            }
            """, []);

    private static string Plan(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("PrivacyErase"))
            .Select(kv => kv.Value)
            .Single();

    [Fact]
    public void ANonNullableString_IsAnonymisedToEmpty()
    {
        var result = Run("""
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Anonymize)]
                    public string FullName { get; set; } = "";
            """);

        Plan(result).Should().Contain("entity.FullName = \"\";");
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG29").Should().BeEmpty();
    }

    /// <summary>The control: null identifies nobody, and a nullable column takes it.</summary>
    [Fact]
    public void ANullableString_IsAnonymisedToNull()
    {
        var result = Run("""
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Anonymize)]
                    public string? Nickname { get; set; }
            """);

        Plan(result).Should().Contain("entity.Nickname = default!;");
    }

    /// <summary>The control: a value type's default is a value, not a null.</summary>
    [Fact]
    public void AValueType_IsAnonymisedToItsDefault()
    {
        var result = Run("""
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Anonymize)]
                    public System.DateOnly BornOn { get; set; }
            """);

        Plan(result).Should().Contain("entity.BornOn = default!;");
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG29").Should().BeEmpty();
    }

    /// <summary>
    ///     A non-nullable reference type other than a string has no value both anonymous and generic:
    ///     reported, and not written — writing null is the defect, and inventing a value would store
    ///     something nobody decided.
    /// </summary>
    [Fact]
    public void ANonNullableReferenceWithNoAnonymousValue_ReportsPrag2912()
    {
        var result = Run("""
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Anonymize)]
                    public byte[] Photo { get; set; } = [];
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG2912").Should().NotBeEmpty();
        Plan(result).Should().NotContain("entity.Photo =");
    }
}
