using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     A column a type inherits is a column it holds, and the privacy reader has to ask
///     about it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The reader enumerates <c>GetMembers()</c>, which returns what a type <b>declares</b>.
///         Measured: turning on the descent into owned records named exactly the four
///         strings declared on <c>LocalIdentity</c> and said nothing about <c>ExternalIdentityKey</c>,
///         <c>CreatedBy</c> and <c>UpdatedBy</c> — public strings on the same owned path, reachable from
///         the same <c>[DataSubject]</c>, differing in one thing: they are declared on the base class.
///     </para>
///     <para>
///         It is not only about owned records. The same call reads the entity's own face, so an entity
///         that inherits a classifiable property was never read either.
///     </para>
/// </remarks>
public class WhatASubjectInheritsTests
{
    private static string[] DiagnosticIds(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG29").Select(d => d.Id).ToArray();

    private static string[] Messages(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG")
            .Select(d => d.Id + ": " + d.GetMessage())
            .ToArray();

    private static string? Generated(SourceGenRunResult result, string marker)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains(marker))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(PrivacyTestSources.Stubs + source, []);

    [Fact]
    public void AnInheritedUnclassifiedString_ReportsPrag2903()
    {
        var result = Run("""

            namespace App
            {
                using Pragmatic.Privacy;

                public abstract class Record
                {
                    public string ExternalKey { get; set; } = "";
                }

                [DataSubject("Number")]
                public class Employee : Record
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Number { get; set; } = "";
                }
            }
            """);

        DiagnosticIds(result).Should().Contain("PRAG2903");
        Messages(result).Should().Contain(m => m.Contains("ExternalKey"));
    }

    /// <summary>
    ///     The control: the same shape with the inherited member ruled on reports nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "it reads the base" is satisfied by a rule that refuses every inherited string —
    ///     which is indistinguishable from a working read on the test above.
    /// </remarks>
    [Fact]
    public void AnInheritedClassifiedMember_ReportsNothing()
    {
        var result = Run("""

            namespace App
            {
                using Pragmatic.Privacy;

                public abstract class Record
                {
                    [NotPersonalData("a composed provider key, assigned by the identity provider")]
                    public string ExternalKey { get; set; } = "";
                }

                [DataSubject("Number")]
                public class Employee : Record
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Number { get; set; } = "";
                }
            }
            """);

        DiagnosticIds(result).Should().BeEmpty();
    }

    /// <summary>
    ///     An inherited classification reaches the erasure plan and the export, not only the diagnostic.
    /// </summary>
    /// <remarks>
    ///     The register and the plan are what the classification is <em>for</em>; a reader that asked
    ///     about the column and then left it out of both would be half a fix.
    /// </remarks>
    [Fact]
    public void AnInheritedClassification_IsInThePlanAndTheExtractor()
    {
        var result = Run("""

            namespace App
            {
                using Pragmatic.Privacy;

                public abstract class Record
                {
                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string? SignInEmail { get; set; }
                }

                [DataSubject("Number")]
                public class Employee : Record
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Number { get; set; } = "";
                }
            }
            """);

        Generated(result, "PrivacyErase")!.Should().Contain("SignInEmail");
        Generated(result, "PrivacyExtract")!.Should().Contain("SignInEmail");
    }

    /// <summary>
    ///     A member the derived type hides with <c>new</c> is read once, and the derived declaration is
    ///     the one that counts.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The derived one is what the compiler binds, so it is what the application writes and reads.
    ///     Two entries for one name would put the same column in the register twice, with two different
    ///     answers about what happens to it on erasure.
    /// </remarks>
    [Fact]
    public void AMemberHiddenWithNew_IsReadFromTheDerivedType()
    {
        var result = Run("""

            namespace App
            {
                using Pragmatic.Privacy;

                public abstract class Record
                {
                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public virtual string? Reference { get; set; }
                }

                [DataSubject("Number")]
                public class Employee : Record
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Number { get; set; } = "";

                    [NotPersonalData("the derived type re-declares it as a machine reference")]
                    public new string? Reference { get; set; }
                }
            }
            """);

        DiagnosticIds(result).Should().BeEmpty();
        Generated(result, "PrivacyErase")!.Should().NotContain("Reference",
            "the derived declaration says it is not personal data, and it is the one the compiler binds");
    }

    /// <summary>
    ///     The control: a base that is not the application's — <c>System.Object</c> and the framework
    ///     types below it — is not walked.
    /// </summary>
    /// <remarks>
    ///     Without it the reader would demand a ruling on every member of every base in the BCL, which
    ///     is both noise and impossible: the classification would have to be written on a type the
    ///     application does not own.
    /// </remarks>
    [Fact]
    public void ABaseInSystem_IsNotAsked()
    {
        var result = Run("""

            namespace App
            {
                using Pragmatic.Privacy;

                [DataSubject("Number")]
                public class Employee : System.Attribute
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Number { get; set; } = "";
                }
            }
            """);

        DiagnosticIds(result).Should().BeEmpty();
    }
}
