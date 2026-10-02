using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Model;

namespace Pragmatic.Email.Model.Tests;

public class EmailSerializerTests
{
    [Fact]
    public void Serialize_EmptyEmail_ProducesValidJson()
    {
        var email = new EmailModel { Subject = "Test" };

        var json = EmailSerializer.Serialize(email);

        json.Should().Contain("\"subject\":\"Test\"");
        json.Should().Contain("\"width\":600");
    }

    [Fact]
    public void Roundtrip_WelcomeEmail_PreservesAllFields()
    {
        var email = new EmailModel
        {
            Subject = "Welcome!",
            Preheader = "Thanks for joining",
            Language = "en-US",
            Width = 600,
            BackgroundColor = "#ffffff",
            WrapperBackgroundColor = "#f4f4f4",
            Sections =
            [
                new EmailSection
                {
                    BackgroundColor = "#007bff",
                    Padding = EmailPadding.All(30),
                    Columns =
                    [
                        new EmailColumn
                        {
                            Content =
                            [
                                new EmailHeadingNode { Content = "Welcome!", Level = 1, Align = EmailTextAlign.Center },
                                new EmailTextNode { Content = "We're glad you're here.", Align = EmailTextAlign.Center, Color = "#ffffff" }
                            ]
                        }
                    ]
                },
                new EmailSection
                {
                    Columns =
                    [
                        new EmailColumn
                        {
                            Content =
                            [
                                new EmailTextNode { Content = "Click below to get started:" },
                                new EmailSpacerNode { Height = 10 },
                                new EmailButtonNode { Text = "Get Started", Href = "https://app.example.com" },
                                new EmailDividerNode(),
                                new EmailImageNode { Source = "https://cdn.example.com/logo.png", Alt = "Logo", Width = 200 }
                            ]
                        }
                    ]
                }
            ]
        };

        var json = EmailSerializer.Serialize(email);
        var deserialized = EmailSerializer.Deserialize(json);

        deserialized.Should().NotBeNull();
        deserialized!.Subject.Should().Be("Welcome!");
        deserialized.Preheader.Should().Be("Thanks for joining");
        deserialized.Sections.Should().HaveCount(2);

        var heroSection = deserialized.Sections[0];
        heroSection.BackgroundColor.Should().Be("#007bff");
        heroSection.Columns[0].Content.Should().HaveCount(2);
        heroSection.Columns[0].Content[0].Should().BeOfType<EmailHeadingNode>();
    }

    [Fact]
    public void Roundtrip_PolymorphicNodes_PreservesTypes()
    {
        var email = new EmailModel
        {
            Sections =
            [
                new EmailSection
                {
                    Columns =
                    [
                        new EmailColumn
                        {
                            Content =
                            [
                                new EmailTextNode { Content = "text" },
                                new EmailHeadingNode { Content = "heading", Level = 2 },
                                new EmailImageNode { Source = "https://img.example.com/photo.jpg", Alt = "Photo" },
                                new EmailButtonNode { Text = "Click", Href = "https://example.com" },
                                new EmailSpacerNode { Height = 15 },
                                new EmailDividerNode { Color = "#999999", Thickness = 2 },
                                new EmailHtmlNode { Html = "<p>raw</p>" }
                            ]
                        }
                    ]
                }
            ]
        };

        var json = EmailSerializer.Serialize(email);
        var deserialized = EmailSerializer.Deserialize(json);

        var content = deserialized!.Sections[0].Columns[0].Content;
        content[0].Should().BeOfType<EmailTextNode>().Which.Content.Should().Be("text");
        content[1].Should().BeOfType<EmailHeadingNode>().Which.Level.Should().Be(2);
        content[2].Should().BeOfType<EmailImageNode>().Which.Source.Should().Contain("photo.jpg");
        content[3].Should().BeOfType<EmailButtonNode>().Which.Href.Should().Be("https://example.com");
        content[4].Should().BeOfType<EmailSpacerNode>().Which.Height.Should().Be(15);
        content[5].Should().BeOfType<EmailDividerNode>().Which.Thickness.Should().Be(2);
        content[6].Should().BeOfType<EmailHtmlNode>().Which.Html.Should().Be("<p>raw</p>");
    }

    [Fact]
    public void Roundtrip_Utf8Bytes_Works()
    {
        var email = new EmailModel
        {
            Subject = "Benvenuto — àéîõü",
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [new EmailTextNode { Content = "Ciao Möndö" }] }] }]
        };

        var bytes = EmailSerializer.SerializeToUtf8(email);
        var deserialized = EmailSerializer.DeserializeFromUtf8(bytes);

        deserialized.Should().NotBeNull();
        deserialized!.Subject.Should().Be("Benvenuto — àéîõü");
    }

    [Fact]
    public void Serialize_ContainsTypeDiscriminator()
    {
        var email = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [new EmailButtonNode { Text = "CTA", Href = "#" }] }] }]
        };

        var json = EmailSerializer.Serialize(email);

        json.Should().Contain("\"$type\":\"button\"");
    }

    [Fact]
    public void Serialize_NullFieldsOmitted()
    {
        var email = new EmailModel();

        var json = EmailSerializer.Serialize(email);

        json.Should().NotContain("\"subject\"");
        json.Should().NotContain("\"preheader\"");
        json.Should().NotContain("\"wrapperBackgroundColor\"");
    }

    [Fact]
    public void Roundtrip_TwoColumnLayout_Works()
    {
        var email = new EmailModel
        {
            Sections =
            [
                new EmailSection
                {
                    Columns =
                    [
                        new EmailColumn { Width = 0.5, Content = [new EmailTextNode { Content = "Left" }] },
                        new EmailColumn { Width = 0.5, Content = [new EmailTextNode { Content = "Right" }] }
                    ]
                }
            ]
        };

        var json = EmailSerializer.Serialize(email);
        var deserialized = EmailSerializer.Deserialize(json);

        deserialized!.Sections[0].Columns.Should().HaveCount(2);
        deserialized.Sections[0].Columns[0].Width.Should().Be(0.5);
        deserialized.Sections[0].Columns[1].Width.Should().Be(0.5);
    }
}
