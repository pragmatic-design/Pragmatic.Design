using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Tests;

/// <summary>
/// Generates themed DOCX files in two ways:
/// 1. Programmatic theme (DocxTheme → styles.xml + theme1.xml)
/// 2. Template-based (generate template DOCX first, then use it as .dotx for content)
/// Output: tests/output/theme-*.docx + template-*.docx
/// </summary>
public class DocxThemeOutputTests
{
    private static readonly string OutputDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "output");


    private static void SaveDocx(byte[] docx, string fileName)
    {
        Directory.CreateDirectory(OutputDir);
        File.WriteAllBytes(Path.Combine(OutputDir, fileName), docx);
    }

    /// <summary>Content-rich document used as the actual content for all theme tests.</summary>
    private static DocumentModel ContentDocument(string themeName) => new DocumentBuilder()
        .Title($"Document with theme {themeName}")
        .Author("Pragmatic S.r.l.")
        .Language("en-US")
        .Page(p => p
            .Header(new TextNode { Content = $"PRAGMATIC S.R.L. — Theme: {themeName}", Style = new NodeStyle { FontSize = 9 } })
            .Heading($"Sample Document — Theme {themeName}", level: 1)
            .Text("This document demonstrates the theme system of the DOCX renderer.")
            .Spacer(3)
            .Heading("Section 1: Content", level: 2)
            .Text("The body text uses the font and size defined by the theme.")
            .Text("The spacing between paragraphs and the line height are controlled by the theme.")
            .Heading("Subsection 1.1: Table", level: 3)
            .Table(t => t
                .Column(width: 80).Column(width: 30).Column(width: 30)
                .HeaderRow("Product", "Quantity", "Price")
                .Row("Widget Pro", "10", "€99.00")
                .Row("Gadget Ultra", "5", "€149.00")
                .Row("Module X", "20", "€49.00"))
            .Spacer(3)
            .Heading("Section 2: Links and Notes", level: 2)
            .Hyperlink("https://pragmatic.design", "Visit the Pragmatic Design site")
            .Text("This paragraph contains a footnote.")
            .Footnote("Explanatory note on the selected theme.")
            .HorizontalRule()
            .Text("End of document.")
            .Footer(new ParagraphNode
            {
                Children = [
                    new TextNode { Content = $"Theme: {themeName} — Page " },
                    new FieldNode { FieldType = FieldType.Page }
                ],
                Style = new NodeStyle { TextAlign = TextAlign.Center, FontSize = 9 }
            })
        )
        .Build();

    /// <summary>Minimal document used to generate the template (only styles matter).</summary>
    private static DocumentModel TemplateShell() => new DocumentBuilder()
        .Title("Template")
        .Page(p => p.Text(" "))
        .Build();

    /// <summary>Generate a template DOCX with the given theme, then use it to render content.</summary>
    private static byte[] RenderWithTemplate(DocxTheme theme, string themeName)
    {
        // Step 1: Generate a "template" DOCX with the theme (contains styles.xml + theme1.xml)
        var templateBytes = DocxRenderer.Render(TemplateShell(), options: new DocxRenderOptions { Theme = theme });

        // Step 2: Use that template to render the actual content document
        return DocxRenderer.Render(ContentDocument(themeName), options: new DocxRenderOptions
        {
            Template = templateBytes
        });
    }

    // --- Programmatic theme (path A) ---

    [Fact]
    public void Output_DefaultTheme_Programmatic()
    {
        var docx = DocxRenderer.Render(ContentDocument("Default"));
        docx.Should().NotBeEmpty();
        SaveDocx(docx, "theme-default.docx");
    }

    [Fact]
    public void Output_FormalTheme_Programmatic()
    {
        var docx = DocxRenderer.Render(ContentDocument("Formal"),
            options: new DocxRenderOptions { Theme = DocxTheme.Formal });
        docx.Should().NotBeEmpty();
        SaveDocx(docx, "theme-formal.docx");
    }

    [Fact]
    public void Output_ModernTheme_Programmatic()
    {
        var docx = DocxRenderer.Render(ContentDocument("Modern"),
            options: new DocxRenderOptions { Theme = DocxTheme.Modern });
        docx.Should().NotBeEmpty();
        SaveDocx(docx, "theme-modern.docx");
    }

    [Fact]
    public void Output_MinimalTheme_Programmatic()
    {
        var docx = DocxRenderer.Render(ContentDocument("Minimal"),
            options: new DocxRenderOptions { Theme = DocxTheme.Minimal });
        docx.Should().NotBeEmpty();
        SaveDocx(docx, "theme-minimal.docx");
    }

    // --- Template-based (path B): generate a template, then use it ---

    [Fact]
    public void Output_FormalTheme_ViaTemplate()
    {
        var docx = RenderWithTemplate(DocxTheme.Formal, "Formal (template)");
        docx.Should().NotBeEmpty();
        SaveDocx(docx, "template-formal.docx");

        // Verify the template's styles were used (not regenerated)
        var stylesXml = DocxTestHelper.GetPart(docx, "word/styles.xml").ToString();
        stylesXml.Should().Contain("Times New Roman"); // from Formal theme
        stylesXml.Should().Contain("Georgia");
    }

    [Fact]
    public void Output_ModernTheme_ViaTemplate()
    {
        var docx = RenderWithTemplate(DocxTheme.Modern, "Modern (template)");
        docx.Should().NotBeEmpty();
        SaveDocx(docx, "template-modern.docx");

        var stylesXml = DocxTestHelper.GetPart(docx, "word/styles.xml").ToString();
        stylesXml.Should().Contain("Segoe UI");
    }

    [Fact]
    public void Output_MinimalTheme_ViaTemplate()
    {
        var docx = RenderWithTemplate(DocxTheme.Minimal, "Minimal (template)");
        docx.Should().NotBeEmpty();
        SaveDocx(docx, "template-minimal.docx");

        var stylesXml = DocxTestHelper.GetPart(docx, "word/styles.xml").ToString();
        stylesXml.Should().Contain("Arial");
    }
}
