namespace Pragmatic.Storage.Tests;

public class MimeTypesTests
{
    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".svg", "image/svg+xml")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".doc", "application/msword")]
    [InlineData(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(".xls", "application/vnd.ms-excel")]
    [InlineData(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(".csv", "text/csv")]
    [InlineData(".txt", "text/plain")]
    [InlineData(".json", "application/json")]
    [InlineData(".xml", "application/xml")]
    [InlineData(".zip", "application/zip")]
    [InlineData(".mp4", "video/mp4")]
    [InlineData(".mp3", "audio/mpeg")]
    public void GetMimeType_WithLeadingDot_ReturnsMappedType(string extension, string expected)
    {
        MimeTypes.GetMimeType(extension).Should().Be(expected);
    }

    [Theory]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("pdf", "application/pdf")]
    [InlineData("txt", "text/plain")]
    public void GetMimeType_WithoutLeadingDot_ReturnsMappedType(string extension, string expected)
    {
        MimeTypes.GetMimeType(extension).Should().Be(expected);
    }

    [Theory]
    [InlineData("JPG", "image/jpeg")]
    [InlineData(".PnG", "image/png")]
    [InlineData(".PDF", "application/pdf")]
    [InlineData("Json", "application/json")]
    public void GetMimeType_IsCaseInsensitive(string extension, string expected)
    {
        MimeTypes.GetMimeType(extension).Should().Be(expected);
    }

    [Theory]
    [InlineData(".bin")]
    [InlineData("xyz")]
    [InlineData(".unknownext")]
    public void GetMimeType_UnknownExtension_ReturnsOctetStream(string extension)
    {
        MimeTypes.GetMimeType(extension).Should().Be("application/octet-stream");
    }

    [Fact]
    public void GetMimeType_Null_ReturnsOctetStream()
    {
        MimeTypes.GetMimeType(null).Should().Be("application/octet-stream");
    }

    [Fact]
    public void GetMimeType_EmptyString_ReturnsOctetStream()
    {
        MimeTypes.GetMimeType(string.Empty).Should().Be("application/octet-stream");
    }
}
