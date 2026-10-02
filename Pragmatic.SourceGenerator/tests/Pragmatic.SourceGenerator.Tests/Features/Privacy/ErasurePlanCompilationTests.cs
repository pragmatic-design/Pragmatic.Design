using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     The erasure plan has to <em>compile</em> in the shapes a Pragmatic application actually produces.
/// </summary>
/// <remarks>
///     Asserting on the text of the plan cannot see either of these defects: both produce output that
///     reads correctly and that the C# compiler rejects. The assertion is therefore on the compilation
///     that results from running the generator, not on the emitted string.
/// </remarks>
public class ErasurePlanCompilationTests
{
    private static void ShouldCompile(SourceGenRunResult result)
    {
        var errors = string.Join(
            "\n", GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString()));

        GeneratorTestHelper.HasCompilationErrors(result).Should()
            .BeFalse("the generated erasure plan must compile, but:\n{0}", errors);
    }

    private static string? ErasurePlan(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("PrivacyErase"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    [Fact]
    public void EntityWithNothingRetained_StillProducesACompilablePlan()
    {
        // Retaining nothing is the ordinary case — an entity whose every classified field is erased.
        // The list of retained fields is then empty, and an empty array with no element to infer a type
        // from is not a valid expression.
        var source = PrivacyTestSources.Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Id { get; set; } = "";

                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string Email { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        ShouldCompile(result);
        ErasurePlan(result).Should().NotBeNull();
    }

    [Fact]
    public void EntityWithPrivateSetters_ErasesThroughTheGeneratedSetter()
    {
        // The shape the framework recommends: setters are private and every write goes through the
        // internal Set{Property} that Pragmatic.Persistence generates. Hand-written here so the test
        // exercises the erasure plan alone — every persistence output is gated on HasPersistenceEFCore,
        // which this compilation deliberately does not satisfy.
        var source = PrivacyTestSources.Stubs + PrivacyTestSources.EntityAttributeStub + """

            namespace App
            {
                using Pragmatic.Privacy;
                using Pragmatic.Persistence.Entity;

                [DataSubject("Id")]
                [Entity]
                public partial class Customer
                {
                    public System.Guid Id { get; private set; }

                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string Email { get; private set; } = "";

                    [PersonalData(DataCategory.Financial, Erasure = ErasureStrategy.Retain,
                                  Reason = "Art. 2220 c.c.")]
                    public string Iban { get; private set; } = "";

                    internal void SetEmail(string value) => Email = value;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        ShouldCompile(result);
        ErasurePlan(result).Should().NotBeNull();
        ErasurePlan(result)!.Should().Contain(
            "entity.SetEmail(",
            "a private setter is written through the generated setter, as every other write in the framework is");
    }

    [Fact]
    public void UnwritablePropertyOnANonEntity_IsReportedRatherThanEmitted()
    {
        // No entity, so no generated setter, so nothing can write the field. Emitting an assignment
        // would not compile and emitting silence would drop the erasure without a word: the plan says
        // nothing and PRAG2907 does the talking.
        var source = PrivacyTestSources.Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Id { get; set; } = "";

                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string Email { get; private set; } = "";

                    // Retains something, so this case does not also depend on the empty-list correction.
                    [PersonalData(DataCategory.Financial, Erasure = ErasureStrategy.Retain,
                                  Reason = "Art. 2220 c.c.")]
                    public string Iban { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        ShouldCompile(result);
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG2907").Should()
            .NotBeEmpty("a field the plan cannot write is an erasure that would never happen");
    }
}
