using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.I18n.Diagnostics;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Generator;

/// <summary>
///     Tests for I18N source generator diagnostics (PRAG1800-1803).
/// </summary>
public class I18NDiagnosticsTests : I18NGeneratorTestBase
{
    // ────────────────────────────────────────────
    // Diagnostic descriptor validation
    // ────────────────────────────────────────────

    [Fact]
    public void MissingTranslationKey_HasCorrectDescriptor()
    {
        I18NDiagnostics.MissingTranslationKey.Id.Should().Be("PRAG1802");
        I18NDiagnostics.MissingTranslationKey.DefaultSeverity.Should().Be(DiagnosticSeverity.Warning);
        I18NDiagnostics.MissingTranslationKey.IsEnabledByDefault.Should().BeTrue();
    }

    [Fact]
    public void EmptyTranslationFile_HasCorrectDescriptor()
    {
        I18NDiagnostics.EmptyTranslationFile.Id.Should().Be("PRAG1803");
        I18NDiagnostics.EmptyTranslationFile.DefaultSeverity.Should().Be(DiagnosticSeverity.Info);
        I18NDiagnostics.EmptyTranslationFile.IsEnabledByDefault.Should().BeTrue();
    }

    // ────────────────────────────────────────────
    // ────────────────────────────────────────────
    // PRAG1801: the same key defined twice for one culture
    // ────────────────────────────────────────────

    /// <summary>
    ///     Two files of the same culture defining one key is reported, and the report says which value
    ///     survives.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This diagnostic does not merely warn: it <b>decides</b>, and its message states the
    ///     decision — the last definition wins. A rule that picks a winner and is asserted by nothing is
    ///     a rule nobody can rely on, because which file is last is a property of enumeration order and
    ///     nothing pins it.
    /// </remarks>
    [Fact]
    public void PRAG1801_TheSameKeyInTwoFilesOfOneCulture_IsReported()
    {
        var first = """
                    {
                        "welcome": "Welcome!"
                    }
                    """;

        var second = """
                     {
                         "welcome": "Welcome back!"
                     }
                     """;

        // Both under a recognised folder and both the same culture — structure A, one folder per
        // culture. A second file outside translations/ i18n/ locales/ lang/ is not a translation file
        // at all, so it would not collide with anything.
        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en/common.json", first),
            ("/app/translations/en/extra.json", second));

        var duplicates = diagnostics.Where(d => d.Id == "PRAG1801").ToList();
        duplicates.Should().ContainSingle("one key is defined twice for one culture");

        var message = duplicates[0].GetMessage();
        message.Should().Contain("welcome", "the message has to name the key that collided");
        message.Should().Contain("en", "and the culture it collided in");
    }

    /// <summary>
    ///     The control: one definition per culture is not a duplicate, however many cultures there are.
    /// </summary>
    /// <remarks>
    ///     Without it, "a duplicate is reported" is satisfied by a rule that reports every key — which
    ///     would fire on every translated application and be switched off within a day.
    /// </remarks>
    [Fact]
    public void PRAG1801_OneDefinitionPerCulture_IsNotADuplicate()
    {
        var enJson = """
                     {
                         "welcome": "Welcome!"
                     }
                     """;

        var itJson = """
                     {
                         "welcome": "Benvenuto!"
                     }
                     """;

        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en.json", enJson),
            ("/app/translations/it.json", itJson));

        diagnostics.Where(d => d.Id == "PRAG1801").Should().BeEmpty(
            "the same key in two different cultures is a translation, not a collision");
    }

    // PRAG1802: Missing translation key across cultures
    // ────────────────────────────────────────────

    [Fact]
    public void PRAG1802_KeyInDefaultButMissingInOtherCulture_EmitsWarning()
    {
        var enJson = """
                     {
                         "welcome": "Welcome!",
                         "errors": {
                             "notFound": "Not found"
                         }
                     }
                     """;

        // Italian file is missing "errors.notFound"
        var itJson = """
                     {
                         "welcome": "Benvenuto!"
                     }
                     """;

        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en.json", enJson),
            ("/app/translations/it.json", itJson));

        var missingKeyDiags = diagnostics.Where(d => d.Id == "PRAG1802").ToList();
        missingKeyDiags.Should().ContainSingle();

        var diag = missingKeyDiags[0];
        diag.Severity.Should().Be(DiagnosticSeverity.Warning);
        var message = diag.GetMessage();
        message.Should().Contain("errors.notFound");
        message.Should().Contain("en");
        message.Should().Contain("it");
    }

    [Fact]
    public void PRAG1802_AllKeysPresent_NoWarning()
    {
        var enJson = """
                     {
                         "welcome": "Welcome!",
                         "goodbye": "Goodbye!"
                     }
                     """;

        var itJson = """
                     {
                         "welcome": "Benvenuto!",
                         "goodbye": "Arrivederci!"
                     }
                     """;

        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en.json", enJson),
            ("/app/translations/it.json", itJson));

        diagnostics.Where(d => d.Id == "PRAG1802").Should().BeEmpty();
    }

    [Fact]
    public void PRAG1802_MultipleKeysMissing_EmitsMultipleWarnings()
    {
        var enJson = """
                     {
                         "welcome": "Welcome!",
                         "goodbye": "Goodbye!",
                         "help": "Help"
                     }
                     """;

        // Italian is missing both "goodbye" and "help"
        var itJson = """
                     {
                         "welcome": "Benvenuto!"
                     }
                     """;

        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en.json", enJson),
            ("/app/translations/it.json", itJson));

        var missingKeyDiags = diagnostics.Where(d => d.Id == "PRAG1802").ToList();
        missingKeyDiags.Should().HaveCount(2);
        missingKeyDiags.Select(d => d.GetMessage()).Should().Contain(m => m.Contains("goodbye"));
        missingKeyDiags.Select(d => d.GetMessage()).Should().Contain(m => m.Contains("help"));
    }

    [Fact]
    public void PRAG1802_ExtraKeyInNonDefaultCulture_NoWarning()
    {
        var enJson = """
                     {
                         "welcome": "Welcome!"
                     }
                     """;

        // Italian has an extra key not in English -- this should NOT trigger a warning
        // because we only warn about keys missing from non-default cultures
        var itJson = """
                     {
                         "welcome": "Benvenuto!",
                         "extraKey": "Chiave extra"
                     }
                     """;

        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en.json", enJson),
            ("/app/translations/it.json", itJson));

        diagnostics.Where(d => d.Id == "PRAG1802").Should().BeEmpty();
    }

    [Fact]
    public void PRAG1802_MultipleCulturesWithMissingKeys_EmitsPerCulture()
    {
        var enJson = """
                     {
                         "welcome": "Welcome!",
                         "goodbye": "Goodbye!"
                     }
                     """;

        var itJson = """
                     {
                         "welcome": "Benvenuto!"
                     }
                     """;

        var deJson = """
                     {
                         "welcome": "Willkommen!"
                     }
                     """;

        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en.json", enJson),
            ("/app/translations/it.json", itJson),
            ("/app/translations/de.json", deJson));

        var missingKeyDiags = diagnostics.Where(d => d.Id == "PRAG1802").ToList();

        // "goodbye" missing in both it and de
        missingKeyDiags.Should().HaveCount(2);
        missingKeyDiags.Should().Contain(d => d.GetMessage().Contains("it"));
        missingKeyDiags.Should().Contain(d => d.GetMessage().Contains("de"));
    }

    // ────────────────────────────────────────────
    // PRAG1803: Empty translation file
    // ────────────────────────────────────────────

    [Fact]
    public void PRAG1803_FileWithOnlyNestedObjects_EmitsInfo()
    {
        var json = """
                   {
                       "section": {
                           "subsection": {}
                       }
                   }
                   """;

        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en.json", json));

        var emptyDiags = diagnostics.Where(d => d.Id == "PRAG1803").ToList();
        emptyDiags.Should().ContainSingle();
        emptyDiags[0].Severity.Should().Be(DiagnosticSeverity.Info);
        emptyDiags[0].GetMessage().Should().Contain("en.json");
    }

    [Fact]
    public void PRAG1803_EmptyJsonObject_EmitsInfo()
    {
        var json = "{}";

        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en.json", json));

        var emptyDiags = diagnostics.Where(d => d.Id == "PRAG1803").ToList();
        emptyDiags.Should().ContainSingle();
        emptyDiags[0].GetMessage().Should().Contain("en.json");
    }

    [Fact]
    public void PRAG1803_FileWithValidKeys_NoInfo()
    {
        var json = """
                   {
                       "welcome": "Welcome!"
                   }
                   """;

        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en.json", json));

        diagnostics.Where(d => d.Id == "PRAG1803").Should().BeEmpty();
    }

    [Fact]
    public void PRAG1803_FileWithNestedStringValues_NoInfo()
    {
        var json = """
                   {
                       "errors": {
                           "notFound": "Not found"
                       }
                   }
                   """;

        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en.json", json));

        diagnostics.Where(d => d.Id == "PRAG1803").Should().BeEmpty();
    }

    [Fact]
    public void PRAG1803_InvalidJson_DoesNotEmit()
    {
        // Invalid JSON should be caught by PRAG1800, not PRAG1803
        var diagnostics = RunGeneratorAndGetDiagnostics(
            ("/app/translations/en.json", "not valid json"));

        diagnostics.Where(d => d.Id == "PRAG1803").Should().BeEmpty();
        diagnostics.Where(d => d.Id == "PRAG1800").Should().ContainSingle();
    }

    // ────────────────────────────────────────────
    // PRAG1804 — a MessageKey nobody translated
    // ────────────────────────────────────────────

    /// <summary>
    ///     The message a caller reads lives under '{key}.detail' — the bare key would collide with
    ///     its own '.title' sibling in the generated class, and the resolver does not read it.
    /// </summary>
    private const string TranslationFile = """
        { "edition": { "full": { "detail": "No seats left" } } }
        """;

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

    /// <summary>
    ///     The resolver takes MessageKey verbatim, so a key nobody defined returns the default text
    ///     in every language without failing. Two consumer projects lost an hour each to it — one
    ///     wrote "error.edition.full" against a JSON key of "edition.full".
    /// </summary>
    [Fact]
    public void MessageKeyWithNoTranslation_ReportsPrag1804()
    {
        var result = RunGeneratorWithSource(
            ErrorWith("error.edition.full"),
            ("translations/en.json", TranslationFile));

        var reported = result.Diagnostics.Where(d => d.Id == "PRAG1804").ToList();

        reported.Should().ContainSingle("the key does not exist in any translation file");
        reported[0].GetMessage().Should().Contain("EditionFullError").And.Contain("error.edition.full");
    }

    [Fact]
    public void MessageKeyThatMatches_ReportsNothing()
    {
        var result = RunGeneratorWithSource(
            ErrorWith("edition.full"),
            ("translations/en.json", TranslationFile));

        // Without this the test above would pass on a generator that reports for every key.
        result.Diagnostics.Should().NotContain(d => d.Id == "PRAG1804");
    }

    /// <summary>
    ///     The check moves with the resolver: a file defining the bare key gives the caller nothing,
    ///     because the message is read from '{key}.detail'. Without this the rename would have left a
    ///     check that passes on files the runtime cannot read.
    /// </summary>
    [Fact]
    public void MessageKeyTranslatedWithoutTheDetailSuffix_ReportsPrag1804()
    {
        var result = RunGeneratorWithSource(
            ErrorWith("edition.full"),
            ("translations/en.json", """{ "edition": { "full": "No seats left" } }"""));

        result.Diagnostics.Should().Contain(d => d.Id == "PRAG1804",
            "the resolver reads 'edition.full.detail', which this file does not define");
    }

    /// <summary>
    ///     A rule deriving from BusinessRuleError names itself through the base constructor and
    ///     inherits a computed MessageKey — so checking only for a literal MessageKey property would
    ///     be blind to exactly the shape the cookbook now recommends.
    /// </summary>
    [Fact]
    public void BusinessRuleDerivedType_WithUnknownRule_ReportsPrag1804()
    {
        var source = """
            namespace TestApp;

            public abstract record Error { public virtual string MessageKey => ""; }

            public record BusinessRuleError : Error
            {
                public BusinessRuleError() { }
                protected BusinessRuleError(string rule) { Rule = rule; }
                public string? Rule { get; init; }
            }

            public sealed record WorksiteClosedError : BusinessRuleError
            {
                public WorksiteClosedError() : base("worksite-closed") { }
            }
            """;

        var result = RunGeneratorWithSource(source, ("translations/en.json", TranslationFile));

        result.Diagnostics.Should().Contain(d => d.Id == "PRAG1804",
            "the rule reaches MessageKey through the base, and no translation defines it");
    }

    // ────────────────────────────────────────────
    // PRAG1804 — an error that overrides only its Code
    // ────────────────────────────────────────────

    /// <summary>
    ///     The shape the documentation shows and every error in the consumer application uses.
    /// </summary>
    /// <remarks>
    ///     <c>Error.MessageKey</c> is virtual with a derived default —
    ///     <c>"error." + Code.ToLowerInvariant().Replace('_', '.')</c> — so an error overriding
    ///     <c>Code</c> alone has a key nobody had ever read. The base declares both members the way
    ///     the runtime does, because what is being measured is which of them the generator follows.
    /// </remarks>
    private static string ErrorWithCode(string code, string? messageKey = null)
    {
        var key = messageKey is null
            ? ""
            : $"""    public override string MessageKey => "{messageKey}";""";

        return $$"""
            namespace TestApp;

            public abstract record Error
            {
                public virtual string Code => "";
                public virtual string MessageKey => "error." + Code.ToLowerInvariant().Replace('_', '.');
            }

            public sealed record EditionFullError : Error
            {
                public override string Code => "{{code}}";
            {{key}}
            }
            """;
    }

    /// <summary>
    ///     An error that names only its code is checked against the key the runtime will derive.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The file defines <c>edition.full.detail</c> and the runtime reads
    ///     <c>error.edition.full.detail</c>, so this is exactly the typo the check exists to catch —
    ///     and the one it could not see, because it only ever read an explicit <c>MessageKey</c>.
    /// </remarks>
    [Fact]
    public void CodeOnlyError_WithNoTranslationForTheDerivedKey_ReportsPrag1804()
    {
        var result = RunGeneratorWithSource(
            ErrorWithCode("EDITION_FULL"),
            ("translations/en.json", TranslationFile));

        var reported = result.Diagnostics.Where(d => d.Id == "PRAG1804").ToList();

        reported.Should().ContainSingle(
            "the key the runtime derives is 'error.edition.full', and no file defines its detail");
        reported[0].GetMessage().Should().Contain("EditionFullError").And.Contain("error.edition.full");
    }

    /// <summary>
    ///     The control: the derived key, translated, is not reported.
    /// </summary>
    /// <remarks>
    ///     Without it the case above is satisfied by a check that reports every error it can see,
    ///     which is the same as reporting nothing.
    /// </remarks>
    [Fact]
    public void CodeOnlyError_WithTheDerivedKeyTranslated_ReportsNothing()
    {
        var result = RunGeneratorWithSource(
            ErrorWithCode("EDITION_FULL"),
            ("translations/en.json",
                """{ "error": { "edition": { "full": { "detail": "No seats left" } } } }"""));

        result.Diagnostics.Should().NotContain(d => d.Id == "PRAG1804");
    }

    /// <summary>
    ///     The second control: an explicit <c>MessageKey</c> still wins.
    /// </summary>
    /// <remarks>
    ///     It is the override, and it is what the runtime reads. An error naming both must be judged
    ///     on the key it declares, never on the one its code would have derived — otherwise the check
    ///     would report a key the application does not use.
    /// </remarks>
    [Fact]
    public void ErrorOverridingBoth_IsJudgedOnItsMessageKey()
    {
        var result = RunGeneratorWithSource(
            ErrorWithCode("EDITION_FULL", messageKey: "edition.full"),
            ("translations/en.json", TranslationFile));

        result.Diagnostics.Should().NotContain(d => d.Id == "PRAG1804",
            "the declared key is 'edition.full' and the file defines its detail; the derived "
            + "'error.edition.full' is what the runtime would NOT read");
    }

    /// <summary>
    ///     And a code that is not a literal stays unread.
    /// </summary>
    /// <remarks>
    ///     The same rule a computed <c>MessageKey</c> has always had: there is nothing to compare, and
    ///     a guess would be worse than silence — it would report a key the application never uses and
    ///     teach its author to ignore the diagnostic.
    /// </remarks>
    [Fact]
    public void ACodeThatIsNotALiteral_IsNotChecked()
    {
        var source = """
            namespace TestApp;

            public abstract record Error
            {
                public virtual string Code => "";
                public virtual string MessageKey => "error." + Code.ToLowerInvariant().Replace('_', '.');
            }

            public sealed record EditionFullError : Error
            {
                private const string Prefix = "EDITION";
                public override string Code => Prefix + "_FULL";
            }
            """;

        var result = RunGeneratorWithSource(source, ("translations/en.json", TranslationFile));

        result.Diagnostics.Should().NotContain(d => d.Id == "PRAG1804");
    }

    // ────────────────────────────────────────────
    // Helper: run generator and collect diagnostics
    // ────────────────────────────────────────────

}
