using Pragmatic.Documents.Email;
using Pragmatic.Email.Model;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Email.Tests;

/// <summary>
///     The plain-text body of a mail, from the same model as its HTML.
/// </summary>
/// <remarks>
///     A mail that offers only HTML is shown as an attachment by some clients and scored as spam by
///     others. Every application wrote this body by hand — Casework walked the model itself, Invoicing
///     and the Showcase kept a second copy of the words — so the two drifted the first time one changed.
/// </remarks>
public sealed class EmailTextRendererTests
{
    [Fact]
    public void TheWords_ComeOutInOrder_OneBlockPerParagraph()
    {
        var text = EmailTextRenderer.Render(Mail(
            new EmailHeadingNode { Content = "Invoice 2026/0001" },
            new EmailTextNode { Content = "Your invoice is attached." }));

        text.Should().Be("Invoice 2026/0001\n\nYour invoice is attached.");
    }

    [Fact]
    public void AButton_KeepsWhereItLeads()
    {
        var text = EmailTextRenderer.Render(Mail(
            new EmailButtonNode { Text = "Pay now", Href = "https://pay.example.test/1" }));

        text.Should().Be("Pay now: https://pay.example.test/1");
    }

    [Fact]
    public void ATable_IsARowPerLine()
    {
        var table = new EmailTableNode
        {
            Header = new EmailTableRow { Cells = [new EmailTableCell { Content = "Item" }, new EmailTableCell { Content = "Total" }] },
            Rows =
            [
                new EmailTableRow { Cells = [new EmailTableCell { Content = "Room" }, new EmailTableCell { Content = "€ 240,00" }] },
            ],
        };

        EmailTextRenderer.Render(Mail(table)).Should().Be("Item | Total\nRoom | € 240,00");
    }

    /// <summary>Trusted HTML the template let through is text here: its tags go, its words stay.</summary>
    [Fact]
    public void TrustedMarkup_LosesItsTags_AndItsEntities()
    {
        var text = EmailTextRenderer.Render(Mail(
            new EmailTextNode { Content = "<b>Tom &amp; Jerry</b><br>arrive", AllowHtml = true }));

        text.Should().Be("Tom & Jerry\narrive");
    }

    [Fact]
    public void ADivider_IsALine_AndImagesAndSpacersSayNothingWithoutAlt()
    {
        var text = EmailTextRenderer.Render(Mail(
            new EmailTextNode { Content = "Above" },
            new EmailDividerNode(),
            new EmailSpacerNode(),
            new EmailImageNode { Source = "logo.png", Alt = "" },
            new EmailTextNode { Content = "Below" }));

        text.Should().Be("Above\n\n----\n\nBelow");
    }

    private static EmailModel Mail(params EmailNode[] content)
        => new()
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = content }] }],
        };
}
