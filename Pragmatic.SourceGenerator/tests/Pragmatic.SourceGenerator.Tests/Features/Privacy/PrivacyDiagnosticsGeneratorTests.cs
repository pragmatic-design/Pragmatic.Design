using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     End-to-end: the unified generator reads the personal-data classification attributes, works out
///     which entities a data subject reaches, and reports what is missing or contradictory.
/// </summary>
/// <remarks>
///     Attribute types are stubbed in the source so <c>FeatureDetector</c> triggers without the runtime
///     package, matching how the other feature tests work.
/// </remarks>
public class PrivacyDiagnosticsGeneratorTests
{
    private const string Stubs = PrivacyTestSources.Stubs;

    private static string[] DiagnosticIds(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG29").Select(d => d.Id).ToArray();

    [Fact]
    public void NoDataSubjectDeclared_ReportsNothing()
    {
        // The property that makes this feature adoptable at all: an application that has not opted in
        // stays silent, however many unclassified strings it contains.
        var source = Stubs + """

            namespace App
            {
                public class Customer
                {
                    public string Name { get; set; } = "";
                    public string Email { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().BeEmpty();
    }

    [Fact]
    public void Retain_WithoutReason_ReportsPrag2901()
    {
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    public string Id { get; set; } = "";

                    [PersonalData(DataCategory.Financial, Erasure = ErasureStrategy.Retain)]
                    public string Iban { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().Contain("PRAG2901");
    }

    [Fact]
    public void Retain_WithReason_DoesNotReportPrag2901()
    {
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    public string Id { get; set; } = "";

                    [PersonalData(DataCategory.Financial, Erasure = ErasureStrategy.Retain,
                                  Reason = "Art. 2220 c.c. — ten years")]
                    public string Iban { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().NotContain("PRAG2901");
    }

    // The plan says the field will be cleared, and the write fails at SaveChanges against the NOT NULL
    // column — so the erasure does not happen and the subject's request fails, at the moment it is
    // exercised. Found by a wiring test that went red for this reason before the property was declared
    // nullable.
    [Fact]
    public void NullErasure_OnANonNullableProperty_ReportsPrag2909()
    {
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    public string Id { get; set; } = "";

                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string Email { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().Contain("PRAG2909");
    }

    [Fact]
    public void NullErasure_OnANullableProperty_IsSilent()
    {
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    public string Id { get; set; } = "";

                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string? Email { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().NotContain("PRAG2909");
    }

    [Fact]
    public void DestroyKey_WithoutEncryption_ReportsPrag2902()
    {
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    public string Id { get; set; } = "";

                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.DestroyKey)]
                    public string Email { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().Contain("PRAG2902");
    }

    [Fact]
    public void PersonalData_WithNoPathToASubject_ReportsPrag2900()
    {
        // An entity that classifies personal data but no subject reaches: its rows can never be erased,
        // and nothing at runtime would ever say so.
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    public string Id { get; set; } = "";
                }

                public class Orphan
                {
                    [PersonalData(DataCategory.Contact)]
                    public string Email { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().Contain("PRAG2900");
    }

    /// <summary>
    ///     A library — a compilation that declares classified data and no <c>[DataSubject]</c> at all —
    ///     is not told its rows can never be erased.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Reachability is decided where the subject is, and for a package that is never its own
    ///         compilation: the <c>[DataSubject]</c> that owns a framework entity is in the application
    ///         that uses it. ⚠️ Reporting <c>[PersonalData]</c> on such a type as an <b>error</b> would stop
    ///         a package like <c>Pragmatic.Identity.Local</c> from classifying anything but secrets — the
    ///         sign-in address on <c>LocalIdentity</c> would stay unclassified because classifying it
    ///         would not build.
    ///     </para>
    ///     <para>
    ///         ⚠️ This is what the class remark states — "nothing is reported until the compilation
    ///         declares a <c>[DataSubject]</c>" — and this test holds PRAG2900 to it.
    ///     </para>
    ///     <para>
    ///         The cost, stated: an <b>application</b> that classifies personal data and forgets its
    ///         subject entirely is not refused. It is the one case this gives up, and a real
    ///         application declares a subject — without one the erasure plan, the register and the
    ///         extractors it is all for are not generated either.
    ///     </para>
    /// </remarks>
    [Fact]
    public void PersonalData_InACompilationWithNoSubjectAtAll_IsNotReported()
    {
        var source = Stubs + """

            namespace Framework
            {
                using Pragmatic.Privacy;

                public class OwnedByWhoeverUsesThis
                {
                    [PersonalData(DataCategory.Contact)]
                    public string Email { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().NotContain("PRAG2900");
    }

    [Fact]
    public void UnclassifiedString_OnAReachableEntity_ReportsPrag2903()
    {
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    public string Id { get; set; } = "";
                    public string Nickname { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().Contain("PRAG2903");
    }

    /// <summary>
    ///     An explicit "no" is a decision, and PRAG2903 asks for a decision.
    /// </summary>
    /// <remarks>
    ///     The diagnostic's own message offers "classify it, or mark it as non-personal", and
    ///     [NotPersonalData] is the second half. Without it the only way to satisfy the diagnostic would
    ///     be to classify a tenant id as something it is not — a wrong entry in the processing register,
    ///     which is worse than a missing one.
    /// </remarks>
    [Fact]
    public void AnExplicitlyNonPersonalString_DoesNotDemandAClassification()
    {
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity)]
                    public string Id { get; set; } = "";

                    [NotPersonalData("Tenant isolation key, assigned by the platform.")]
                    public string TenantId { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().NotContain("PRAG2903");
    }

    [Fact]
    public void NonStringProperty_DoesNotDemandAClassification()
    {
        // Asking for a ruling on every int would bury the decisions that matter.
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity)]
                    public string Id { get; set; } = "";

                    public int LoyaltyPoints { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().NotContain("PRAG2903");
    }

    [Fact]
    public void LinksToSubject_NamingAMissingProperty_ReportsPrag2906()
    {
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity)]
                    public string Id { get; set; } = "";
                }

                [LinksToSubject("NoSuchProperty")]
                public class Order
                {
                    [PersonalData(DataCategory.Contact)]
                    public string Email { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().Contain("PRAG2906");
    }

    // =========================================================================
    // Generated output
    // =========================================================================

    private static string? Generated(SourceGenRunResult result, string hintFragment)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains(hintFragment))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    [Fact]
    public void ReachableEntity_GetsAnExtractorCoveringItsClassifiedFields()
    {
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity)]
                    public string Id { get; set; } = "";

                    [PersonalData(DataCategory.Contact)]
                    public string Email { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var extractor = Generated(result, "PrivacyExtract");

        extractor.Should().NotBeNull("a reachable entity with personal data needs an access projection");
        extractor!.Should().Contain("CustomerPersonalDataExtractor")
            .And.Contain("values[\"Id\"]")
            .And.Contain("values[\"Email\"]");
    }

    [Fact]
    public void NoDataSubject_GeneratesNothing()
    {
        // Silence means silence: no diagnostics and no generated files either.
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                public class Customer
                {
                    [PersonalData(DataCategory.Contact)]
                    public string Email { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        Generated(result, "PrivacyExtract").Should().BeNull();
        Generated(result, "PrivacyErase").Should().BeNull();
    }

    [Fact]
    public void ErasurePlan_AppliesEachStrategyAndStatesWhatIsRetained()
    {
        var source = Stubs + """

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

                    [PersonalData(DataCategory.Financial, Erasure = ErasureStrategy.Retain,
                                  Reason = "Art. 2220 c.c.")]
                    public string Iban { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var plan = Generated(result, "PrivacyErase");

        plan.Should().NotBeNull();
        plan!.Should()
            .Contain("entity.Email = default!;", "a Null field is cleared")
            .And.Contain("subjectRef", "a Pseudonymize field takes the subject's pseudonym")
            .And.Contain("Art. 2220 c.c.", "a retained field must state why, and it reaches the subject");
    }

    [Fact]
    public void ErasurePlan_RecordsWhenTheRowGoesAndWhenTheKeyGoes()
    {
        // Row deletion and key destruction are decisions about the row and the subject, not about a
        // field, so the plan reports them rather than trying to perform them.
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Delete)]
                    public string Id { get; set; } = "";

                    [PersonalData(DataCategory.Special, Erasure = ErasureStrategy.DestroyKey, Encrypted = true)]
                    public string Diagnosis { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var plan = Generated(result, "PrivacyErase");

        plan.Should().NotBeNull();
        plan!.Should()
            .Contain("RequiresRowDeletion = true")
            .And.Contain("RequiresKeyDestruction = true");
    }

    /// <summary>The control: a subject nothing encrypts says no, and its erasure needs no key.</summary>
    [Fact]
    public void ErasureStep_OfASubjectThatEncryptsNothing_NeedsNoKey()
    {
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Reader
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Id { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var plan = Generated(result, "PrivacyErase");

        plan.Should().NotBeNull();
        plan!.Should().Contain("RequiresKeyDestruction = false",
            "without it the refusal would fire on every application that erases by clearing columns");
    }

    [Fact]
    public void ProcessingRegisterMetadata_DescribesWhatIsProcessedAndWhatIsRetained()
    {
        // The half of the Article 30 register that can be derived from the code. The other half —
        // controller and purpose — stays configuration, because inventing it would produce a document
        // that reads as authoritative and is not.
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity)]
                    public string Id { get; set; } = "";

                    [PersonalData(DataCategory.Financial, Erasure = ErasureStrategy.Retain,
                                  Reason = "Art. 2220 c.c.")]
                    public string Iban { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var metadata = Generated(result, "_Metadata.PersonalData");

        metadata.Should().NotBeNull();
        metadata!.Should()
            .Contain("MetadataCategory.PersonalData", "the category is named, not a numeric cast")
            .And.Contain("App.Customer")
            .And.Contain("Financial")
            .And.Contain("Art. 2220 c.c.", "the register has to carry why something is kept");
    }

    [Fact]
    public void ProcessingRegisterMetadata_SurvivesAReasonContainingQuotes()
    {
        // A retention reason is free text somebody typed. An unescaped quote would not corrupt one
        // field, it would end the JSON string early and take the whole payload with it.
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Financial, Erasure = ErasureStrategy.Retain,
                                  Reason = "see \"annex B\", clause 4\\5")]
                    public string Iban { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should()
            .BeFalse("a quote in a reason must not break the generated metadata");
    }

    [Fact]
    public void LinkedEntity_WithAValidPath_IsReachableAndNotReportedAsOrphan()
    {
        var source = Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Id")]
                public class Customer
                {
                    [PersonalData(DataCategory.Identity)]
                    public string Id { get; set; } = "";
                }

                [LinksToSubject("Customer")]
                public class Order
                {
                    public Customer Customer { get; set; } = null!;

                    [PersonalData(DataCategory.Contact)]
                    public string Email { get; set; } = "";
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().NotContain("PRAG2900");
        DiagnosticIds(result).Should().NotContain("PRAG2906");
    }
}
