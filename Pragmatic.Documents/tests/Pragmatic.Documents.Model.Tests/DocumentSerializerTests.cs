using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Model.Tests;

public class DocumentSerializerTests
{
    [Fact]
    public void Serialize_EmptyDocument_ProducesValidJson()
    {
        var doc = new DocumentModel { Title = "Test" };

        var json = DocumentSerializer.Serialize(doc);

        json.Should().Contain("\"title\":\"Test\"");
        json.Should().Contain("\"pageSize\":"); // enum serialized
    }

    [Fact]
    public void Roundtrip_SimpleDocument_PreservesAllFields()
    {
        var doc = new DocumentModel
        {
            Title = "Invoice #123",
            Author = "Pragmatic",
            Language = "it-IT",
            PageSize = PageSize.A4,
            Orientation = PageOrientation.Portrait,
            Margins = new Margins { Top = 20, Right = 15, Bottom = 20, Left = 15 },
            Pages =
            [
                new DocumentPage
                {
                    Content =
                    [
                        new HeadingNode { Content = "Invoice", Level = 1 },
                        new TextNode { Content = "Thank you for your purchase." },
                        new SpacerNode { Height = 5 },
                        new HorizontalRuleNode { Thickness = 0.3 }
                    ]
                }
            ]
        };

        var json = DocumentSerializer.Serialize(doc);
        var deserialized = DocumentSerializer.Deserialize(json);

        deserialized.Should().NotBeNull();
        deserialized!.Title.Should().Be("Invoice #123");
        deserialized.Author.Should().Be("Pragmatic");
        deserialized.Language.Should().Be("it-IT");
        deserialized.Pages.Should().HaveCount(1);
        deserialized.Pages[0].Content.Should().HaveCount(4);
    }

    [Fact]
    public void Roundtrip_PolymorphicNodes_PreservesTypes()
    {
        var doc = new DocumentModel
        {
            Pages =
            [
                new DocumentPage
                {
                    Content =
                    [
                        new TextNode { Content = "text" },
                        new HeadingNode { Content = "heading", Level = 2 },
                        new ImageNode { Source = "logo.png", Alt = "Logo", Width = 50 },
                        new PageBreakNode(),
                        new BarcodeNode { Value = "ABC-123", Type = BarcodeType.Code128 }
                    ]
                }
            ]
        };

        var json = DocumentSerializer.Serialize(doc);
        var deserialized = DocumentSerializer.Deserialize(json);

        var content = deserialized!.Pages[0].Content;
        content[0].Should().BeOfType<TextNode>().Which.Content.Should().Be("text");
        content[1].Should().BeOfType<HeadingNode>().Which.Level.Should().Be(2);
        content[2].Should().BeOfType<ImageNode>().Which.Source.Should().Be("logo.png");
        content[3].Should().BeOfType<PageBreakNode>();
        content[4].Should().BeOfType<BarcodeNode>().Which.Type.Should().Be(BarcodeType.Code128);
    }

    [Fact]
    public void Roundtrip_TableNode_PreservesStructure()
    {
        var doc = new DocumentModel
        {
            Pages =
            [
                new DocumentPage
                {
                    Content =
                    [
                        new TableNode
                        {
                            Columns = [new TableColumn { Width = 50 }, new TableColumn { Width = 100 }],
                            Header = new TableRow
                            {
                                Cells =
                                [
                                    new TableCell { Content = [new TextNode { Content = "Item" }] },
                                    new TableCell { Content = [new TextNode { Content = "Price" }] }
                                ]
                            },
                            Rows =
                            [
                                new TableRow
                                {
                                    Cells =
                                    [
                                        new TableCell { Content = [new TextNode { Content = "Widget" }] },
                                        new TableCell { Content = [new TextNode { Content = "€99.00" }] }
                                    ]
                                }
                            ],
                            RepeatHeader = true
                        }
                    ]
                }
            ]
        };

        var json = DocumentSerializer.Serialize(doc);
        var deserialized = DocumentSerializer.Deserialize(json);

        var table = deserialized!.Pages[0].Content[0].Should().BeOfType<TableNode>().Subject;
        table.Columns.Should().HaveCount(2);
        table.Header.Should().NotBeNull();
        table.Header!.Cells.Should().HaveCount(2);
        table.Rows.Should().HaveCount(1);
        table.RepeatHeader.Should().BeTrue();
    }

    [Fact]
    public void Roundtrip_ListNode_PreservesStructure()
    {
        var doc = new DocumentModel
        {
            Pages =
            [
                new DocumentPage
                {
                    Content =
                    [
                        new ListNode
                        {
                            Ordered = true,
                            Items =
                            [
                                new ListItem { Content = [new TextNode { Content = "First" }] },
                                new ListItem { Content = [new TextNode { Content = "Second" }] }
                            ]
                        }
                    ]
                }
            ]
        };

        var json = DocumentSerializer.Serialize(doc);
        var deserialized = DocumentSerializer.Deserialize(json);

        var list = deserialized!.Pages[0].Content[0].Should().BeOfType<ListNode>().Subject;
        list.Ordered.Should().BeTrue();
        list.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Roundtrip_Utf8Bytes_Works()
    {
        var doc = new DocumentModel
        {
            Title = "UTF-8 Test — àéîõü",
            Pages = [new DocumentPage { Content = [new TextNode { Content = "Héllo Wörld" }] }]
        };

        var bytes = DocumentSerializer.SerializeToUtf8(doc);
        var deserialized = DocumentSerializer.DeserializeFromUtf8(bytes);

        deserialized.Should().NotBeNull();
        deserialized!.Title.Should().Be("UTF-8 Test — àéîõü");
    }

    [Fact]
    public void Serialize_NullFieldsOmitted()
    {
        var doc = new DocumentModel { Title = "Only Title" };

        var json = DocumentSerializer.Serialize(doc);

        json.Should().NotContain("\"author\"");
        json.Should().NotContain("\"metadata\"");
        json.Should().NotContain("\"language\"");
    }

    [Fact]
    public void Serialize_ContainsTypeDiscriminator()
    {
        var doc = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [new TextNode { Content = "test" }] }]
        };

        var json = DocumentSerializer.Serialize(doc);

        json.Should().Contain("\"$type\":\"text\"");
    }

    [Fact]
    public void Roundtrip_NodeStyle_PreservesProperties()
    {
        var doc = new DocumentModel
        {
            Pages =
            [
                new DocumentPage
                {
                    Content =
                    [
                        new TextNode
                        {
                            Content = "Styled",
                            Style = new NodeStyle
                            {
                                FontFamily = "Helvetica",
                                FontSize = 14,
                                FontWeight = FontWeight.Bold,
                                Color = "#ff0000",
                                TextAlign = TextAlign.Center,
                                MarginTop = 10,
                                BackgroundColor = "#eeeeee"
                            }
                        }
                    ]
                }
            ]
        };

        var json = DocumentSerializer.Serialize(doc);
        var deserialized = DocumentSerializer.Deserialize(json);

        var text = deserialized!.Pages[0].Content[0] as TextNode;
        text!.Style.Should().NotBeNull();
        text.Style!.FontFamily.Should().Be("Helvetica");
        text.Style.FontWeight.Should().Be(FontWeight.Bold);
        text.Style.Color.Should().Be("#ff0000");
    }
}
