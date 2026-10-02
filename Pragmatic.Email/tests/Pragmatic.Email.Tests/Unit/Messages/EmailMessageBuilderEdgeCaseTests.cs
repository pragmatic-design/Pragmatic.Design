using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Builder;

namespace Pragmatic.Email.Tests.Unit.Messages;

public sealed class EmailMessageBuilderEdgeCaseTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_WithBlankAddress_Throws(string address)
    {
        var act = () => new EmailMessageBuilder().From(address);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("@example.com")]
    [InlineData("local@")]
    public void To_WithMalformedAddress_Throws(string address)
    {
        var act = () => new EmailMessageBuilder().To(address);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Cc_WithMalformedAddress_Throws()
    {
        var act = () => new EmailMessageBuilder().Cc("invalid");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Bcc_WithMalformedAddress_Throws()
    {
        var act = () => new EmailMessageBuilder().Bcc("invalid");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ReplyTo_WithMalformedAddress_Throws()
    {
        var act = () => new EmailMessageBuilder().ReplyTo("invalid");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_WithEmptyAttachmentData_Succeeds()
    {
        var message = new EmailMessageBuilder()
            .From("sender@example.com")
            .To("r@example.com")
            .Subject("Test")
            .TextBody("Body")
            .Attach("empty.txt", ReadOnlyMemory<byte>.Empty, "text/plain")
            .Build();

        message.Attachments.Should().HaveCount(1);
        message.Attachments[0].Data.Length.Should().Be(0);
    }

    [Fact]
    public void Build_WithBothTextAndHtmlBody_KeepsBoth()
    {
        var message = new EmailMessageBuilder()
            .From("sender@example.com")
            .To("r@example.com")
            .Subject("Test")
            .TextBody("plain")
            .HtmlBody("<b>html</b>")
            .Build();

        message.TextBody.Should().Be("plain");
        message.HtmlBody.Should().Be("<b>html</b>");
    }

    [Fact]
    public void Build_WithDuplicateHeaderName_LastValueWins()
    {
        var message = new EmailMessageBuilder()
            .From("sender@example.com")
            .To("r@example.com")
            .Subject("Test")
            .TextBody("Body")
            .Header("X-Custom", "first")
            .Header("X-Custom", "second")
            .Build();

        message.Headers["X-Custom"].Should().Be("second");
    }

    [Fact]
    public void Build_CanBeCalledTwice_ProducingEquivalentMessages()
    {
        var builder = new EmailMessageBuilder()
            .From("sender@example.com")
            .To("r@example.com")
            .Subject("Test")
            .TextBody("Body");

        var first = builder.Build();
        var second = builder.Build();

        first.Subject.Should().Be(second.Subject);
        first.To.Should().BeEquivalentTo(second.To);
    }
}
