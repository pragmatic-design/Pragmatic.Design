using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Model.Tests;

public class NewNodeSerializationTests
{
    [Fact]
    public void Roundtrip_HyperlinkNode_PreservesFields()
    {
        var doc = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new HyperlinkNode
                {
                    Href = "https://pragmatic.design",
                    Children = [new TextNode { Content = "Visit us" }]
                }
            ] }]
        };

        var json = DocumentSerializer.Serialize(doc);
        var result = DocumentSerializer.Deserialize(json)!;

        var link = result.Pages[0].Content[0].Should().BeOfType<HyperlinkNode>().Subject;
        link.Href.Should().Be("https://pragmatic.design");
        link.Children.Should().HaveCount(1);
    }

    [Fact]
    public void Roundtrip_TocNode_PreservesFields()
    {
        var doc = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new TocNode { MaxLevel = 4, Title = "Sommario" }
            ] }]
        };

        var json = DocumentSerializer.Serialize(doc);
        var result = DocumentSerializer.Deserialize(json)!;

        var toc = result.Pages[0].Content[0].Should().BeOfType<TocNode>().Subject;
        toc.MaxLevel.Should().Be(4);
        toc.Title.Should().Be("Sommario");
    }

    [Fact]
    public void Roundtrip_FootnoteNode_PreservesFields()
    {
        var doc = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new FootnoteNode { Content = "See reference [1]." }
            ] }]
        };

        var json = DocumentSerializer.Serialize(doc);
        var result = DocumentSerializer.Deserialize(json)!;

        var fn = result.Pages[0].Content[0].Should().BeOfType<FootnoteNode>().Subject;
        fn.Content.Should().Be("See reference [1].");
    }

    [Fact]
    public void Roundtrip_FieldNode_PreservesFields()
    {
        var doc = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new FieldNode { FieldType = FieldType.Page },
                new FieldNode { FieldType = FieldType.Date, Format = "dd/MM/yyyy" }
            ] }]
        };

        var json = DocumentSerializer.Serialize(doc);
        var result = DocumentSerializer.Deserialize(json)!;

        var page = result.Pages[0].Content[0].Should().BeOfType<FieldNode>().Subject;
        page.FieldType.Should().Be(FieldType.Page);

        var date = result.Pages[0].Content[1].Should().BeOfType<FieldNode>().Subject;
        date.FieldType.Should().Be(FieldType.Date);
        date.Format.Should().Be("dd/MM/yyyy");
    }

    [Fact]
    public void Roundtrip_BookmarkNode_PreservesFields()
    {
        var doc = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new BookmarkNode
                {
                    Name = "section-1",
                    Children = [new TextNode { Content = "Section 1 content" }]
                }
            ] }]
        };

        var json = DocumentSerializer.Serialize(doc);
        var result = DocumentSerializer.Deserialize(json)!;

        var bm = result.Pages[0].Content[0].Should().BeOfType<BookmarkNode>().Subject;
        bm.Name.Should().Be("section-1");
        bm.Children.Should().HaveCount(1);
    }

    [Fact]
    public void Roundtrip_DocumentModel_NewMetadata()
    {
        var doc = new DocumentModel
        {
            Title = "Test",
            Subject = "Testing",
            Keywords = "test, unit, model",
            CreatedDate = new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero)
        };

        var json = DocumentSerializer.Serialize(doc);
        var result = DocumentSerializer.Deserialize(json)!;

        result.Subject.Should().Be("Testing");
        result.Keywords.Should().Be("test, unit, model");
        result.CreatedDate.Should().NotBeNull();
    }

    [Fact]
    public void Roundtrip_HeadingNode_WithChildren()
    {
        var doc = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new HeadingNode
                {
                    Content = "Chapter 1",
                    Level = 1,
                    Children = [
                        new TextNode { Content = "Chapter " },
                        new TextNode { Content = "1", Style = new NodeStyle { FontWeight = FontWeight.Bold } }
                    ]
                }
            ] }]
        };

        var json = DocumentSerializer.Serialize(doc);
        var result = DocumentSerializer.Deserialize(json)!;

        var heading = result.Pages[0].Content[0].Should().BeOfType<HeadingNode>().Subject;
        heading.Children.Should().HaveCount(2);
        heading.Content.Should().Be("Chapter 1");
    }

    [Fact]
    public void Roundtrip_ListItem_WithSubList()
    {
        var doc = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new ListNode
                {
                    Ordered = true,
                    Items = [
                        new ListItem
                        {
                            Content = [new TextNode { Content = "Item 1" }],
                            SubList = new ListNode
                            {
                                Ordered = false,
                                Items = [new ListItem { Content = [new TextNode { Content = "Sub-item A" }] }]
                            }
                        }
                    ]
                }
            ] }]
        };

        var json = DocumentSerializer.Serialize(doc);
        var result = DocumentSerializer.Deserialize(json)!;

        var list = result.Pages[0].Content[0].Should().BeOfType<ListNode>().Subject;
        list.Items[0].SubList.Should().NotBeNull();
        list.Items[0].SubList!.Items.Should().HaveCount(1);
    }

    [Fact]
    public void Roundtrip_NodeStyle_NewProperties()
    {
        var doc = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new TextNode
                {
                    Content = "Styled",
                    Style = new NodeStyle
                    {
                        Strikethrough = true,
                        VerticalPosition = VerticalPosition.Superscript,
                        LineHeight = 1.5,
                        FirstLineIndent = 10,
                        HighlightColor = "yellow",
                        LetterSpacing = 0.5,
                        BorderStyle = BorderStyle.Dashed,
                        CellVerticalAlign = CellVerticalAlign.Middle
                    }
                }
            ] }]
        };

        var json = DocumentSerializer.Serialize(doc);
        var result = DocumentSerializer.Deserialize(json)!;

        var style = result.Pages[0].Content[0].Style!;
        style.Strikethrough.Should().BeTrue();
        style.VerticalPosition.Should().Be(VerticalPosition.Superscript);
        style.LineHeight.Should().Be(1.5);
        style.FirstLineIndent.Should().Be(10);
        style.HighlightColor.Should().Be("yellow");
        style.LetterSpacing.Should().Be(0.5);
        style.BorderStyle.Should().Be(BorderStyle.Dashed);
        style.CellVerticalAlign.Should().Be(CellVerticalAlign.Middle);
    }

    [Fact]
    public void Roundtrip_DocumentPage_FirstPageHeaderFooter()
    {
        var doc = new DocumentModel
        {
            Pages = [new DocumentPage
            {
                DifferentFirstPage = true,
                FirstPageHeader = [new TextNode { Content = "Cover Header" }],
                FirstPageFooter = [new TextNode { Content = "Cover Footer" }],
                Header = [new TextNode { Content = "Normal Header" }],
                Footer = [new TextNode { Content = "Normal Footer" }],
                Content = [new TextNode { Content = "Body" }]
            }]
        };

        var json = DocumentSerializer.Serialize(doc);
        var result = DocumentSerializer.Deserialize(json)!;

        var page = result.Pages[0];
        page.DifferentFirstPage.Should().BeTrue();
        page.FirstPageHeader.Should().HaveCount(1);
        page.FirstPageFooter.Should().HaveCount(1);
    }
}
