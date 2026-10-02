using System.Globalization;
using System.Text.Json;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Markup.Tests;

/// <summary>
///     A template by name, with its data and a language, in one call: the model of a document, or the
///     subject, HTML and text of a mail.
/// </summary>
/// <remarks>
///     What this replaces is the plumbing every application wrote by hand around the parsers — find the
///     file, parse, a resolver with a partial provider over the same files, a culture scope, the HTML
///     renderer, a plain-text body. The templates here are embedded in this test assembly, the way a
///     module ships its own.
/// </remarks>
public sealed class PdxTemplatesTests
{
    private static readonly DateTime IssuedOn = new(2026, 3, 14);

    private readonly PdxTemplates _templates = new(
        new EmbeddedPdxTemplateSource(typeof(PdxTemplatesTests).Assembly),
        new StringLocalizer(Translations(), new I18NOptions()));

    [Fact]
    public async Task ADocument_IsResolvedWithItsImportedPartial()
    {
        var document = await _templates.DocumentAsync("receipt.pdxdoc", "en", Data());

        var text = JsonSerializer.Serialize(document.Model);
        text.Should().Contain("Corner Shop", "the letterhead is imported from the same source");
        text.Should().Contain("Receipt");
    }

    /// <summary>
    ///     The language given is the language of the translations <b>and</b> of the pipes.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The resolver translates with the <b>ambient</b> culture and formats with the data
    ///     context's: a caller who set only <c>data.WithCulture("it")</c> got Italian dates in English
    ///     sentences. Here the ambient culture is English on purpose, which is what makes the assertion
    ///     mean something.
    /// </remarks>
    [Fact]
    public async Task TheLanguageGiven_GovernsTranslationsAndFormats()
    {
        var document = await I18NContext.WithCultureAsync(
            "en", () => _templates.DocumentAsync("receipt.pdxdoc", "it", Data()).AsTask());

        var text = JsonSerializer.Serialize(document.Model);
        text.Should().Contain("Ricevuta", "the translation is the Italian one");
        text.Should().Contain(IssuedOn.ToString("d", CultureInfo.GetCultureInfo("it")),
            "and so is the date format");
        text.Should().NotContain("Receipt");
    }

    [Fact]
    public async Task WhatTheTemplateAskedForAndDidNotGet_IsReported()
    {
        var document = await _templates.DocumentAsync("receipt.pdxdoc", "en", Data());

        document.Warnings.Should().Contain(w => w.Path.Contains("receipt.missing"));
    }

    /// <summary>
    ///     A translation nobody wrote is reported too, not only a data path nobody provided.
    /// </summary>
    /// <remarks>
    ///     A missing key renders as the key itself — <c>nobody.wrote.this</c> in the reader's letter — and
    ///     it is not a data path, so the resolver's own warnings never saw it. It is the failure a module
    ///     whose translation files never reached the localizer produces on every line.
    /// </remarks>
    [Fact]
    public async Task ATranslationNobodyWrote_IsReported()
    {
        var document = await _templates.DocumentAsync("untranslated.pdxdoc", "en", Data());

        document.Warnings.Should().Contain(w => w.Path == "t:nobody.wrote.this");
        document.Warnings.Should().NotContain(w => w.Path == "t:receipt.title", "that one has a translation");
    }

    [Fact]
    public async Task AMail_HasItsSubjectHtmlAndText()
    {
        var mail = await _templates.EmailAsync("welcome.pdxemail", "it", Data());

        mail.Subject.Should().Be("Benvenuto, Ada");
        mail.Preheader.Should().Be("Tutto pronto");
        mail.Html.Should().Contain("Corner Shop", "the header partial comes from the same source");
        mail.Html.Should().Contain("Ciao Ada");
        mail.Text.Should().Contain("Ciao Ada");
        mail.Text.Should().NotContain("<", "the text body is words, not markup");
    }

    /// <summary>A value from the data is text in the mail, never markup.</summary>
    [Fact]
    public async Task AValueThatLooksLikeMarkup_IsEncodedInTheHtml()
    {
        var data = Data(customer: "<b>Ada</b>");

        var mail = await _templates.EmailAsync("welcome.pdxemail", "en", data);

        mail.Html.Should().NotContain("<b>Ada</b>");
        mail.Html.Should().Contain("&lt;b&gt;Ada&lt;/b&gt;");
    }

    [Fact]
    public async Task AMissingTemplate_NamesItAndWhereItWasLookedFor()
    {
        var missing = async () => await _templates.DocumentAsync("nowhere.pdxdoc", "en", Data());

        var thrown = await missing.Should().ThrowAsync<PdxTemplateNotFoundException>();
        thrown.Which.Message.Should().Contain("nowhere.pdxdoc");
        thrown.Which.Message.Should().Contain(nameof(EmbeddedPdxTemplateSource));
    }

    private static TemplateDataContext Data(string customer = "Ada")
        => new TemplateDataContext()
            .AddSource("shop", new Dictionary<string, object?> { ["name"] = "Corner Shop" })
            .AddSource("customer", new Dictionary<string, object?> { ["name"] = customer })
            .AddSource("receipt", new Dictionary<string, object?> { ["issuedOn"] = IssuedOn });

    private static InMemoryLocalizationProvider Translations()
        => new InMemoryLocalizationProvider()
            .AddString("en", "receipt.title", "Receipt")
            .AddString("it", "receipt.title", "Ricevuta")
            .AddString("en", "receipt.issued", "Issued on {when}")
            .AddString("it", "receipt.issued", "Emessa il {when}")
            .AddString("en", "welcome.subject", "Welcome, {name}")
            .AddString("it", "welcome.subject", "Benvenuto, {name}")
            .AddString("en", "welcome.preheader", "All set")
            .AddString("it", "welcome.preheader", "Tutto pronto")
            .AddString("en", "welcome.body", "Hello {name}")
            .AddString("it", "welcome.body", "Ciao {name}")
            .AddString("en", "welcome.start", "Start")
            .AddString("it", "welcome.start", "Inizia");
}
