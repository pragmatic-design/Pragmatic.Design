using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Tests;

public class DocxThemeTests
{

    private static DocumentModel SampleDocument() => new DocumentBuilder()
        .Title("Theme Test")
        .Author("Pragmatic")
        .Page(p => p
            .Heading("Heading Level 1", level: 1)
            .Text("Body text with the default font and color.")
            .Heading("Heading Level 2", level: 2)
            .Text("More body text to show paragraph spacing.")
            .Heading("Heading Level 3", level: 3)
            .Table(t => t
                .Column(width: 80).Column(width: 40)
                .HeaderRow("Item", "Value")
                .Row("Alpha", "100")
                .Row("Beta", "200"))
            .HorizontalRule()
            .Hyperlink("https://pragmatic.design", "Visit Pragmatic")
            .Footnote("A themed footnote.")
        )
        .Build();

    [Fact]
    public void DefaultTheme_ProducesValidDocx()
    {
        var docx = DocxRenderer.Render(SampleDocument());

        docx.Should().NotBeEmpty();
        DocxTestHelper.HasPart(docx, "word/theme/theme1.xml").Should().BeTrue();
    }

    [Fact]
    public void FormalTheme_UsesSerifFonts()
    {
        var options = new DocxRenderOptions { Theme = DocxTheme.Formal };
        var docx = DocxRenderer.Render(SampleDocument(), options: options);

        var stylesXml = DocxTestHelper.GetPart(docx, "word/styles.xml").ToString();
        stylesXml.Should().Contain("Times New Roman");
        stylesXml.Should().Contain("Georgia");
    }

    [Fact]
    public void ModernTheme_UsesSegoeUI()
    {
        var options = new DocxRenderOptions { Theme = DocxTheme.Modern };
        var docx = DocxRenderer.Render(SampleDocument(), options: options);

        var stylesXml = DocxTestHelper.GetPart(docx, "word/styles.xml").ToString();
        stylesXml.Should().Contain("Segoe UI");
    }

    [Fact]
    public void MinimalTheme_UsesArial()
    {
        var options = new DocxRenderOptions { Theme = DocxTheme.Minimal };
        var docx = DocxRenderer.Render(SampleDocument(), options: options);

        var stylesXml = DocxTestHelper.GetPart(docx, "word/styles.xml").ToString();
        stylesXml.Should().Contain("Arial");
    }

    [Fact]
    public void CustomTheme_OverridesColors()
    {
        var theme = new DocxTheme
        {
            PrimaryColor = "FF0000",
            SecondaryColor = "AA0000",
            AccentColor = "00FF00",
            HyperlinkColor = "0000FF"
        };
        var options = new DocxRenderOptions { Theme = theme };
        var docx = DocxRenderer.Render(SampleDocument(), options: options);

        // Theme XML contains accent and hyperlink colors
        var themeXml = DocxTestHelper.GetPart(docx, "word/theme/theme1.xml").ToString();
        themeXml.Should().Contain("00FF00"); // accent1
        themeXml.Should().Contain("0000FF"); // hlink

        // Styles XML contains heading colors from PrimaryColor
        var stylesXml = DocxTestHelper.GetPart(docx, "word/styles.xml").ToString();
        stylesXml.Should().Contain("FF0000"); // heading H1-H2 color
    }

    [Fact]
    public void Theme_ContainsColorScheme()
    {
        var docx = DocxRenderer.Render(SampleDocument());

        var themeXml = DocxTestHelper.GetPart(docx, "word/theme/theme1.xml").ToString();
        themeXml.Should().Contain("clrScheme");
        themeXml.Should().Contain("fontScheme");
        themeXml.Should().Contain("fmtScheme");
    }

    [Fact]
    public void Theme_ContainsFontScheme()
    {
        var options = new DocxRenderOptions { Theme = DocxTheme.Formal };
        var docx = DocxRenderer.Render(SampleDocument(), options: options);

        var themeXml = DocxTestHelper.GetPart(docx, "word/theme/theme1.xml").ToString();
        themeXml.Should().Contain("Georgia"); // majorFont
        themeXml.Should().Contain("Times New Roman"); // minorFont
    }

    [Fact]
    public void NoTheme_UsesDefaultTheme()
    {
        var docx = DocxRenderer.Render(SampleDocument(), options: new DocxRenderOptions());

        var stylesXml = DocxTestHelper.GetPart(docx, "word/styles.xml").ToString();
        stylesXml.Should().Contain("Calibri");
    }
}
