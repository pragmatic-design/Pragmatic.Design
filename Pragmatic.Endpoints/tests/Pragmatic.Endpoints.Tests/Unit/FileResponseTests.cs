using Pragmatic.Testing.Assertions;
using Pragmatic.Endpoints.Responses;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class FileResponseTests
{
    [Fact]
    public void Constructor_SetsContent()
    {
        using var stream = new MemoryStream();
        var response = new FileResponse(stream, "application/pdf");

        response.Content.Should().BeSameAs(stream);
    }

    [Fact]
    public void Constructor_SetsContentType()
    {
        using var stream = new MemoryStream();
        var response = new FileResponse(stream, "application/pdf");

        response.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public void FileName_DefaultIsNull()
    {
        using var stream = new MemoryStream();
        var response = new FileResponse(stream, "text/plain");

        response.FileName.Should().BeNull();
    }

    [Fact]
    public void FileName_CanBeSet()
    {
        using var stream = new MemoryStream();
        var response = new FileResponse(stream, "application/pdf", "report.pdf");

        response.FileName.Should().Be("report.pdf");
    }

    [Fact]
    public void EnableRangeProcessing_DefaultIsFalse()
    {
        using var stream = new MemoryStream();
        var response = new FileResponse(stream, "video/mp4");

        response.EnableRangeProcessing.Should().BeFalse();
    }

    [Fact]
    public void Inline_DefaultIsFalse()
    {
        using var stream = new MemoryStream();
        var response = new FileResponse(stream, "application/pdf");

        response.Inline.Should().BeFalse();
    }

    [Fact]
    public void ETag_CanBeSet()
    {
        using var stream = new MemoryStream();
        var response = new FileResponse(stream, "image/png") { ETag = "abc123" };

        response.ETag.Should().Be("abc123");
    }

    [Fact]
    public void LastModified_CanBeSet()
    {
        using var stream = new MemoryStream();
        var date = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var response = new FileResponse(stream, "image/png") { LastModified = date };

        response.LastModified.Should().Be(date);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var stream = new MemoryStream();
        var a = new FileResponse(stream, "text/plain", "file.txt");
        var b = new FileResponse(stream, "text/plain", "file.txt");

        a.Should().Be(b);
    }

    [Fact]
    public void RecordEquality_DifferentContentType_AreNotEqual()
    {
        var stream = new MemoryStream();
        var a = new FileResponse(stream, "text/plain");
        var b = new FileResponse(stream, "application/json");

        a.Should().NotBe(b);
    }
}
