using System.Xml.Linq;
using Pragmatic.Documents.Model;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Docx.Tests;

/// <summary>
/// An image whose source resolves to nothing is an error the caller sees, naming what it looked for and
/// what there was. Leaving the document without it would give a valid .docx, a successful call, and a
/// missing picture that only opening the file reveals.
/// </summary>
public class AnImageThatCannotBeResolvedTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // A 1×1 PNG: the renderer embeds bytes, it does not decode them.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static DocumentModel WithImage(string source, string? alt = null)
        => new DocumentBuilder().Page(p => p.Image(source, width: 20, height: 20, alt: alt)).Build();

    private static int Drawings(byte[] docx)
        => DocxTestHelper.GetPart(docx, "word/document.xml").Descendants(W + "drawing").Count();

    [Fact]
    public void Render_ResourceNameNotAmongTheResources_ThrowsNamingItAndTheAvailableOnes()
    {
        var resources = new DocxResources { ["logo"] = Png, ["stamp"] = Png };

        var act = () => DocxRenderer.Render(WithImage("resource:missing", alt: "Company logo"), resources);

        var error = act.Should().Throw<InvalidOperationException>().Which;
        error.Message.Should().Contain("'missing'");
        error.Message.Should().Contain("Company logo");
        error.Message.Should().Contain("logo");
        error.Message.Should().Contain("stamp");
    }

    [Fact]
    public void Render_ResourceNamePresent_EmbedsExactlyOneDrawing()
    {
        var resources = new DocxResources { ["logo"] = Png };

        var docx = DocxRenderer.Render(WithImage("resource:logo"), resources);

        Drawings(docx).Should().Be(1);
        DocxTestHelper.GetEntryNames(docx).Should().Contain(e => e.StartsWith("word/media/", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_ResourceSourceWithNoResourcesPassed_ThrowsSayingNoneWere()
    {
        var act = () => DocxRenderer.Render(WithImage("resource:logo"));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("no resources");
    }

    [Fact]
    public void Render_MalformedDataUri_ThrowsWithTheFormatErrorInside()
    {
        var act = () => DocxRenderer.Render(WithImage("data:image/png;base64,not*base64"));

        var error = act.Should().Throw<InvalidOperationException>().Which;
        error.InnerException.Should().BeOfType<FormatException>();
        error.Message.Should().Contain("data:");
    }

    [Fact]
    public void Render_WellFormedDataUri_EmbedsExactlyOneDrawing()
    {
        var docx = DocxRenderer.Render(WithImage("data:image/png;base64," + Convert.ToBase64String(Png)));

        Drawings(docx).Should().Be(1);
    }

    [Fact]
    public void Render_UrlSource_ThrowsSayingItIsNotFetched()
    {
        var act = () => DocxRenderer.Render(WithImage("https://cdn.example.com/logo.png"), new DocxResources { ["logo"] = Png });

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("https://cdn.example.com/logo.png");
    }

    [Fact]
    public void Render_BarcodeWithoutItsImage_StillWritesTheValueAsText()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [new BarcodeNode { Value = "INV-42" }] }]
        };

        var docx = DocxRenderer.Render(model);

        DocxTestHelper.GetPart(docx, "word/document.xml").ToString().Should().Contain("[Barcode: INV-42]");
        Drawings(docx).Should().Be(0);
    }
}
