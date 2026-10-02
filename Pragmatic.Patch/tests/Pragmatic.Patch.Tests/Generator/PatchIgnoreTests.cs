using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Patch.Tests.Generator;

/// <summary>
///     A domain invariant can be declared where the patch is declared.
/// </summary>
/// <remarks>
///     The shape of a patch is derived from the entity, so every settable property was patchable and
///     only infrastructure columns were kept out — id, audit, soft-delete, and the tenant, owner and
///     scope keys a patch must never touch. A property the <b>domain</b> forbids correcting could only
///     be refused at runtime, while the published contract went on offering it: the refusal and the
///     contract disagreed, and only the refusal was checked by anything.
/// </remarks>
public class PatchIgnoreTests : PatchGeneratorTestBase
{
    [Fact]
    public void PatchIgnore_OnAProperty_KeepsItOutOfThePatch()
    {
        var result = RunGenerator("""
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            public class Term
            {
                public string Word { get; set; } = "";
                public string Definition { get; set; } = "";
            }

            [GeneratePatch<Term>]
            [PatchIgnore(nameof(Term.Word))]
            public partial record CorrectTermPatch;
            """);

        HasCompilationErrors(result).Should().BeFalse();
        var generated = GetGeneratedSource(result, "CorrectTermPatch.Patch");

        generated.Should().Contain("Definition", "the control: the rest of the patch is unchanged");
        generated.Should().NotContain("Optional<string> Word",
            "the word is what every past sighting was resolved against, so it is not correctable");
    }

    /// <summary>
    ///     A name that matches nothing is reported, not ignored.
    /// </summary>
    /// <remarks>
    ///     Ignoring it does the opposite of what the line says: the property stays in the patch, the
    ///     contract goes on offering it, and the author believes it is protected.
    /// </remarks>
    [Fact]
    public void PatchIgnore_NamingNothing_IsReported()
    {
        var result = RunGenerator("""
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            public class Term { public string Word { get; set; } = ""; }

            [GeneratePatch<Term>]
            [PatchIgnore("Wrod")]
            public partial record CorrectTermPatch;
            """);

        HasDiagnostic(result, "PRAG2206").Should().BeTrue();
    }

    /// <summary>The control: without the attribute nothing is reported and nothing is removed.</summary>
    [Fact]
    public void APatchWithoutTheAttribute_KeepsEveryPropertyAndReportsNothing()
    {
        var result = RunGenerator("""
            using Pragmatic.Patch.Attributes;

            namespace TestApp;

            public class Term { public string Word { get; set; } = ""; }

            [GeneratePatch<Term>]
            public partial record CorrectTermPatch;
            """);

        HasDiagnostic(result, "PRAG2206").Should().BeFalse();
        GetGeneratedSource(result, "CorrectTermPatch.Patch").Should().Contain("Optional<string> Word");
    }
}
