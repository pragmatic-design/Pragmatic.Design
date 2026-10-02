using Microsoft.CodeAnalysis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Generator;

/// <summary>
///     The check that a <c>MessageKey</c> has no translation runs for the application that has
///     translated nothing, which is the one it exists for.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A <c>Generate</c> that opens with <c>if (files.IsDefaultOrEmpty) return;</c> above every
///         diagnostic leaves an application with <b>zero</b> translation files hearing nothing, while one
///         that has started translating gets told — and its error types publish English sentences as
///         their titles with no build ever mentioning it.
///     </para>
///     <para>
///         The guard reads as an obvious optimisation — no files, nothing to parse — and it is right
///         for the four checks that parse files. It is wrong for the one check that is about their
///         absence: <c>PRAG1804</c> needs the error types, not the translations.
///     </para>
///     <para>
///         ⚠️ <c>Info</c>, and the severity is the decision rather than a detail. Every single-language
///         application declares error types and translates none, which is a legitimate shape; a warning
///         would fire on correct code, and a diagnostic that fires on correct code gets suppressed
///         project-wide and stops being read at all.
///     </para>
/// </remarks>
public class AnApplicationThatTranslatedNothingIsToldTests : I18NGeneratorTestBase
{
    private static string ErrorWith(string messageKey) => $$"""
        namespace TestApp;

        public abstract record Error
        {
            public virtual string MessageKey => "";
        }

        public sealed record EditionFullError : Error
        {
            public override string MessageKey => "{{messageKey}}";
        }
        """;

    [Fact]
    public void AnErrorTypeWithNoTranslationFileAtAll_IsReported()
    {
        var result = RunGeneratorWithSource(ErrorWith("edition.full"));

        var reported = result.Diagnostics.Where(d => d.Id == "PRAG1804").ToList();

        reported.Should().ContainSingle(
            "an application that has translated nothing is the one this check exists for; got: ["
            + string.Join(", ", result.Diagnostics.Select(d => d.Id)) + "]");
        reported[0].GetMessage().Should().Contain("EditionFullError").And.Contain("edition.full");
    }

    /// <summary>
    ///     ⚠️ The severity is asserted, not only the id.
    /// </summary>
    /// <remarks>
    ///     <c>Info</c> is the whole decision. A test that checked the id alone would keep passing if
    ///     somebody later raised this to a warning — and every monolingual application building under
    ///     <c>--warnaserror</c> would stop compiling for having done nothing wrong.
    /// </remarks>
    [Fact]
    public void TheReport_IsInformationAndNotAWarning()
    {
        var result = RunGeneratorWithSource(ErrorWith("edition.full"));

        result.Diagnostics.Single(d => d.Id == "PRAG1804")
            .Severity.Should().Be(DiagnosticSeverity.Info);
    }

    /// <summary>
    ///     The control: a translation that covers the key keeps it quiet.
    /// </summary>
    /// <remarks>
    ///     Without it, "the untranslated key is reported" is satisfied by a check that reports every
    ///     key of every application, translated or not.
    /// </remarks>
    [Fact]
    public void AKeyThatIsTranslated_IsNotReported()
    {
        var result = RunGeneratorWithSource(
            ErrorWith("edition.full"),
            ("translations/en.json", """{ "edition": { "full": { "detail": "No seats left" } } }"""));

        result.Diagnostics.Should().NotContain(d => d.Id == "PRAG1804");
    }

    /// <summary>
    ///     The control that separates "nothing is translated" from "there is nothing to translate".
    /// </summary>
    /// <remarks>
    ///     A compilation with no error type at all must say nothing either way — otherwise the check
    ///     would greet every project that references this package with a report about nothing.
    /// </remarks>
    [Fact]
    public void ACompilationWithNoErrorTypes_SaysNothing()
    {
        var result = RunGeneratorWithSource("namespace TestApp; public sealed class Nothing;");

        result.Diagnostics.Should().NotContain(d => d.Id == "PRAG1804");
    }
}
