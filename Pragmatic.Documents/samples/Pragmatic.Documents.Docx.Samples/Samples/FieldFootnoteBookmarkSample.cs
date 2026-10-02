using Pragmatic.Documents.Docx;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Samples.Samples;

/// <summary>
///     Word-specific block nodes in a DOCX context, via the fluent PageBuilder
///     shortcuts: <c>Field</c> (<see cref="FieldNode"/>: page / total pages /
///     date), <c>Footnote</c> (<see cref="FootnoteNode"/>) and <c>Bookmark</c>
///     (<see cref="BookmarkNode"/>). Each maps to native OOXML that Word
///     understands and refreshes on open.
/// </summary>
public static class FieldFootnoteBookmarkSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Fields, footnotes and bookmarks (DOCX) ---");

        var model = new DocumentBuilder()
            .Title("Fields, footnotes & bookmarks")
            .Page(p => p
                .Heading("Fields, Footnotes & Bookmarks", level: 1)

                // FieldNode: values Word computes/refreshes at open time.
                .Text("Generated on:")
                .Field(FieldType.Date, format: "yyyy-MM-dd")

                .Text("This sentence carries an explanatory footnote.")

                // FootnoteNode: superscript marker + note text at the bottom of the page.
                .Footnote("Footnotes render as native OOXML footnotes.")

                // BookmarkNode: a named anchor wrapping block-level content,
                // referenceable from a TOC or cross-reference.
                .Bookmark("appendix",
                    new HeadingNode { Content = "Appendix A", Level = 2 },
                    new TextNode { Content = "Bookmarked block content stays block-level (headings, paragraphs)." })

                // Page numbering via fields.
                .Text("Page numbering:")
                .Field(FieldType.Page)
                .Text("of")
                .Field(FieldType.NumPages))
            .Build();

        var bytes = DocxRenderer.Render(model, options: new DocxRenderOptions { UpdateFieldsOnOpen = true });

        var path = Path.Combine(outputDir, "fields-footnotes-bookmarks.docx");
        File.WriteAllBytes(path, bytes);

        Console.WriteLine($"  fields-footnotes-bookmarks.docx  {bytes.Length} bytes (FieldNode + FootnoteNode + BookmarkNode)");
        Console.WriteLine();
    }
}
