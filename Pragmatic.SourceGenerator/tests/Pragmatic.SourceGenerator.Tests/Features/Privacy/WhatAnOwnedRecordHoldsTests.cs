using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     What an entity owns is part of what it holds: the reader descends into an owned record the same
///     way the redaction map does, so the register, the erasure plan and PRAG2903 see its columns.
/// </summary>
/// <remarks>
///     <para>
///         What this pins is agreement between two mechanisms reading the same declarations.
///         <c>DeclaredRedactor</c> masks <c>Identity.PasswordHash</c> one level down; a privacy reader
///         that stopped at the entity's own face would let a column be masked in the logs and still be
///         absent from the Article 30 register and from the erasure plan — and PRAG2903, the one that
///         decides whether the build passes, would be the silent one.
///     </para>
///     <para>
///         ⚠️ The two walks are not the same question. Redaction asks "what below here is classified";
///         this asks "what below here is an unclassified string that nobody has ruled on", which is an
///         error. They share the depth, the cycle guard and what counts as a type worth entering —
///         <see cref="Pragmatic.SourceGenerator.Core.OwnedMemberWalk" /> — and nothing else.
///     </para>
/// </remarks>
public class WhatAnOwnedRecordHoldsTests
{
    private static string[] DiagnosticIds(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG29").Select(d => d.Id).ToArray();

    /// <summary>
    ///     Every diagnostic the generator reported, not only the privacy ones.
    /// </summary>
    /// <remarks>
    ///     Deliberately the whole PRAG prefix: a walk that throws is reported as PRAG9000 by
    ///     <c>RegisterSourceOutputSafe</c>, and filtering to PRAG29 would show that as "reported
    ///     nothing" — which reads like a rule that did not fire.
    /// </remarks>
    private static string[] Messages(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG")
            .Select(d => d.Id + ": " + d.GetMessage())
            .ToArray();

    private static string? Generated(SourceGenRunResult result, string marker)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains(marker))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    private static void ShouldCompile(SourceGenRunResult result)
    {
        var errors = string.Join(
            "\n", GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString()));

        GeneratorTestHelper.HasCompilationErrors(result).Should()
            .BeFalse("the generated privacy output must compile, but:\n{0}", errors);
    }

    /// <summary>
    ///     The shape Time off has: a subject owning a record whose members are the framework's, not the
    ///     application's.
    /// </summary>
    private const string SubjectOwningARecord = """

        namespace App
        {
            using Pragmatic.Privacy;

            public class Credentials
            {
                [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                public string? SignInEmail { get; set; }

                public string ResetToken { get; set; } = "";
            }

            [DataSubject("Number")]
            public class Employee
            {
                [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                public string Number { get; set; } = "";

                public Credentials? Identity { get; set; }
            }
        }
        """;

    [Fact]
    public void UnclassifiedString_InAnOwnedRecord_ReportsPrag2903NamingThePath()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            PrivacyTestSources.Stubs + SubjectOwningARecord, []);

        DiagnosticIds(result).Should().Contain("PRAG2903");

        // The path, not the leaf: "ResetToken" alone points at a type the reader of the message cannot
        // find — there are as many ResetTokens as there are owned records.
        Messages(result).Should().Contain(m => m.Contains("Identity.ResetToken"));
    }

    /// <summary>
    ///     The control: the same shape with the nested member ruled on reports nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "it descends" is satisfied by a rule that refuses every owned record — which
    ///     would be indistinguishable from a working descent on the test above.
    /// </remarks>
    [Fact]
    public void ClassifiedMember_InAnOwnedRecord_ReportsNothing()
    {
        var source = PrivacyTestSources.Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                public class Credentials
                {
                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string? SignInEmail { get; set; }

                    [NotPersonalData("a hashed, short-lived token; it identifies a request, not a person")]
                    public string ResetToken { get; set; } = "";
                }

                [DataSubject("Number")]
                public class Employee
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Number { get; set; } = "";

                    public Credentials? Identity { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        DiagnosticIds(result).Should().BeEmpty();
    }

    [Fact]
    public void ClassifiedMember_InAnOwnedRecord_IsInTheErasurePlan()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            PrivacyTestSources.Stubs + SubjectOwningARecord, []);

        var plan = Generated(result, "PrivacyErase");

        plan.Should().NotBeNull();
        plan!.Should().Contain("Identity.SignInEmail");

        // An owned reference can be absent — Time off's employee has no account until HR opens one —
        // so the write is guarded. Assigning through a null reference is an erasure that throws while
        // serving an erasure request.
        plan.Should().Contain("entity.Identity is not null");
    }

    [Fact]
    public void ClassifiedMember_InAnOwnedRecord_IsInTheExtractor()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            PrivacyTestSources.Stubs + SubjectOwningARecord, []);

        var extractor = Generated(result, "PrivacyExtract");

        extractor.Should().NotBeNull();
        extractor!.Should().Contain("Identity.SignInEmail");
    }

    /// <summary>The plan and the extractor are code: a path that reads well and does not compile is no use.</summary>
    [Fact]
    public void WhatIsGeneratedForAnOwnedRecord_Compiles()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            PrivacyTestSources.Stubs + SubjectOwningARecord, []);

        ShouldCompile(result);
    }

    /// <summary>
    ///     The control that says the descent stops where another entity begins.
    /// </summary>
    /// <remarks>
    ///     A team is not part of the employee: it has its own row, its own subject path and its own plan.
    ///     Folding its columns into the employee's would report them twice, and erase them from a plan
    ///     that does not own them.
    /// </remarks>
    [Fact]
    public void AnEntityReachedByNavigation_IsNotFoldedIntoTheOwner()
    {
        var source = PrivacyTestSources.Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                [LinksToSubject("Manager")]
                public class Team
                {
                    public string RoomCode { get; set; } = "";

                    public Employee? Manager { get; set; }
                }

                [DataSubject("Number")]
                public class Employee
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Number { get; set; } = "";

                    public Team? Team { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        // RoomCode is the team's own unclassified string: PRAG2903 is reported against the team, where
        // somebody can rule on it — and never a second time through the employee that navigates to it.
        Messages(result).Should().Contain(m => m.Contains("'Team.RoomCode'"));
        Messages(result).Should().NotContain(m => m.Contains("Employee.Team.RoomCode"));
    }

    /// <summary>
    ///     An owned record that points back at its owner terminates, and the classification below it is
    ///     still collected.
    /// </summary>
    /// <remarks>
    ///     Two types pointing at each other is an ordinary intermediate state while somebody wires a
    ///     model up. A generator that hangs there takes the IDE with it.
    /// </remarks>
    [Fact]
    public void AnOwnedRecordThatPointsBack_TerminatesAndStillCollects()
    {
        var source = PrivacyTestSources.Stubs + """

            namespace App
            {
                using Pragmatic.Privacy;

                public class Credentials
                {
                    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
                    public string? SignInEmail { get; set; }

                    public Employee? Owner { get; set; }
                }

                [DataSubject("Number")]
                public class Employee
                {
                    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Pseudonymize)]
                    public string Number { get; set; } = "";

                    public Credentials? Identity { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        Generated(result, "PrivacyErase").Should().NotBeNull();
        Generated(result, "PrivacyErase")!.Should().Contain("Identity.SignInEmail");
    }
}
