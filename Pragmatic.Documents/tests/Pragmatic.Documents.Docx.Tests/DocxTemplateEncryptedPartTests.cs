using Pragmatic.Documents.Model;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Docx.Tests;

/// <summary>
///     A template part the archive marks as encrypted is refused before it is opened. The runtimes disagree on what
///     opening it does: .NET 10 ignores the flag and reads the bytes as styles, .NET 11 throws a
///     <see cref="NotSupportedException" /> for a method it does not know (#932). The refusal is the loader's own, the
///     same on both.
/// </summary>
public class DocxTemplateEncryptedPartTests
{
    private static DocumentModel Shell() => new DocumentBuilder()
        .Title("Template")
        .Page(p => p.Text("Body"))
        .Build();

    [Fact]
    public void Render_TemplateWithEncryptedStyles_IsRefused()
    {
        var template = DocxRenderer.Render(Shell());
        var encrypted = ZipHeaders.MarkEncrypted(template, "word/styles.xml");

        var act = () => DocxRenderer.Render(Shell(), options: new DocxRenderOptions { Template = encrypted });

        act.Should().Throw<InvalidOperationException>().Where(e =>
            e.Message.Contains("DOCX template entry 'word/styles.xml' is encrypted", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_TemplateWithNoEncryptedPart_IsAccepted()
    {
        var template = DocxRenderer.Render(Shell());

        var docx = DocxRenderer.Render(Shell(), options: new DocxRenderOptions { Template = template });

        docx.Should().NotBeEmpty();
    }
}
