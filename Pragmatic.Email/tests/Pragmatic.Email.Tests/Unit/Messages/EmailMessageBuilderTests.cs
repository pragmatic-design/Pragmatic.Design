using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Builder;

namespace Pragmatic.Email.Tests.Unit.Messages;

public sealed class EmailMessageBuilderTests
{
    [Fact]
    public void Build_WithAllRequiredFields_Succeeds()
    {
        var message = new EmailMessageBuilder()
            .From("sender@example.com", "Sender")
            .To("recipient@example.com")
            .Subject("Test")
            .TextBody("Hello")
            .Build();

        message.From.Address.Should().Be("sender@example.com");
        message.To.Should().HaveCount(1);
        message.Subject.Should().Be("Test");
        message.TextBody.Should().Be("Hello");
    }

    [Fact]
    public void Build_WithHtmlBodyOnly_Succeeds()
    {
        var message = new EmailMessageBuilder()
            .From("sender@example.com")
            .To("r@example.com")
            .Subject("Test")
            .HtmlBody("<h1>Hello</h1>")
            .Build();

        message.HtmlBody.Should().Be("<h1>Hello</h1>");
        message.TextBody.Should().BeNull();
    }

    [Fact]
    public void Build_WithoutFrom_Throws()
    {
        var act = () => new EmailMessageBuilder()
            .To("r@example.com")
            .Subject("Test")
            .TextBody("Body")
            .Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*From*");
    }

    [Fact]
    public void Build_WithoutTo_Throws()
    {
        var act = () => new EmailMessageBuilder()
            .From("sender@example.com")
            .Subject("Test")
            .TextBody("Body")
            .Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*To*");
    }

    [Fact]
    public void Build_WithoutSubject_Throws()
    {
        var act = () => new EmailMessageBuilder()
            .From("sender@example.com")
            .To("r@example.com")
            .TextBody("Body")
            .Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Subject*");
    }

    [Fact]
    public void Build_WithoutBody_Throws()
    {
        var act = () => new EmailMessageBuilder()
            .From("sender@example.com")
            .To("r@example.com")
            .Subject("Test")
            .Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Body*");
    }

    [Fact]
    public void Build_WithMultipleRecipients_Works()
    {
        var message = new EmailMessageBuilder()
            .From("sender@example.com")
            .To("a@example.com")
            .To("b@example.com")
            .Cc("cc@example.com")
            .Bcc("bcc@example.com")
            .Subject("Test")
            .TextBody("Body")
            .Build();

        message.To.Should().HaveCount(2);
        message.Cc.Should().HaveCount(1);
        message.Bcc.Should().HaveCount(1);
    }

    [Fact]
    public void Build_WithAttachment_Works()
    {
        var data = new byte[] { 1, 2, 3 };
        var message = new EmailMessageBuilder()
            .From("sender@example.com")
            .To("r@example.com")
            .Subject("Test")
            .TextBody("Body")
            .Attach("file.pdf", data, "application/pdf")
            .Build();

        message.Attachments.Should().HaveCount(1);
        message.Attachments[0].FileName.Should().Be("file.pdf");
        message.Attachments[0].IsInline.Should().BeFalse();
    }

    [Fact]
    public void Build_WithInlineImage_Works()
    {
        var data = new byte[] { 1, 2, 3 };
        var message = new EmailMessageBuilder()
            .From("sender@example.com")
            .To("r@example.com")
            .Subject("Test")
            .HtmlBody("<img src='cid:logo'>")
            .InlineImage("logo", data)
            .Build();

        message.Attachments.Should().HaveCount(1);
        message.Attachments[0].IsInline.Should().BeTrue();
        message.Attachments[0].ContentId.Should().Be("logo");
    }

    [Fact]
    public void Build_WithHeaders_Works()
    {
        var message = new EmailMessageBuilder()
            .From("sender@example.com")
            .To("r@example.com")
            .Subject("Test")
            .TextBody("Body")
            .Header("X-Custom", "value")
            .Build();

        message.Headers.Should().ContainKey("X-Custom");
        message.Headers["X-Custom"].Should().Be("value");
    }

    [Fact]
    public void Build_WithReplyTo_Works()
    {
        var message = new EmailMessageBuilder()
            .From("sender@example.com")
            .To("r@example.com")
            .ReplyTo("reply@example.com", "Reply")
            .Subject("Test")
            .TextBody("Body")
            .Build();

        message.ReplyTo.Should().NotBeNull();
        message.ReplyTo!.Value.Address.Should().Be("reply@example.com");
    }
}
