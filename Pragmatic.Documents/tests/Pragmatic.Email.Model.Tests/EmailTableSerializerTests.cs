using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Model;

namespace Pragmatic.Email.Model.Tests;

public class EmailTableSerializerTests
{
    [Fact]
    public void Roundtrip_EmailTableNode_PreservesStructure()
    {
        var email = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [
                new EmailTableNode
                {
                    Columns = [new EmailTableColumn { Width = 200 }, new EmailTableColumn { Width = 100, Align = EmailTextAlign.Right }],
                    Header = new EmailTableRow
                    {
                        Cells = [new EmailTableCell { Content = "Item", Bold = true }, new EmailTableCell { Content = "Price", Bold = true }]
                    },
                    Rows =
                    [
                        new EmailTableRow { Cells = [new EmailTableCell { Content = "Widget" }, new EmailTableCell { Content = "€99.00" }] },
                        new EmailTableRow { Cells = [new EmailTableCell { Content = "Gadget" }, new EmailTableCell { Content = "€49.00" }] }
                    ],
                    BorderColor = "#cccccc",
                    CellPadding = 10
                }
            ] }] }]
        };

        var json = EmailSerializer.Serialize(email);
        var deserialized = EmailSerializer.Deserialize(json);

        var table = deserialized!.Sections[0].Columns[0].Content[0].Should().BeOfType<EmailTableNode>().Subject;
        table.Columns.Should().HaveCount(2);
        table.Columns[1].Align.Should().Be(EmailTextAlign.Right);
        table.Header.Should().NotBeNull();
        table.Header!.Cells.Should().HaveCount(2);
        table.Header.Cells[0].Content.Should().Be("Item");
        table.Rows.Should().HaveCount(2);
        table.BorderColor.Should().Be("#cccccc");
        table.CellPadding.Should().Be(10);
    }

    [Fact]
    public void Serialize_EmailTableNode_HasTypeDiscriminator()
    {
        var email = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [
                new EmailTableNode { Rows = [new EmailTableRow { Cells = [new EmailTableCell { Content = "test" }] }] }
            ] }] }]
        };

        var json = EmailSerializer.Serialize(email);

        json.Should().Contain("\"$type\":\"table\"");
    }

    [Fact]
    public void Builder_Table_ProducesCorrectModel()
    {
        var email = new EmailBuilder()
            .Subject("Invoice")
            .FullWidthSection(c => c
                .Table(t => t
                    .Column(width: 200)
                    .Column(width: 100, align: EmailTextAlign.Right)
                    .BorderColor("#dddddd")
                    .CellPadding(6)
                    .HeaderRow("Description", "Amount")
                    .Row("Room Deluxe", "€450.00")
                    .Row("Breakfast", "€90.00")
                )
            )
            .Build();

        var table = email.Sections[0].Columns[0].Content[0].Should().BeOfType<EmailTableNode>().Subject;
        table.Columns.Should().HaveCount(2);
        table.Header!.Cells.Should().HaveCount(2);
        table.Header.Cells[0].Bold.Should().BeTrue();
        table.Rows.Should().HaveCount(2);
        table.BorderColor.Should().Be("#dddddd");
    }
}
