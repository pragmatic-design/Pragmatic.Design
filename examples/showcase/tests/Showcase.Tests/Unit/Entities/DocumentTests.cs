using Pragmatic.Testing.Assertions;
using Showcase.Billing.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests for the Document polymorphic attachment entity.
/// Demonstrates [PolymorphicAttachment] and [Attachable] usage.
/// </summary>
public class DocumentTests
{
    [Fact]
    public void Create_SetsFileName()
    {
        var doc = new Document();
        doc.SetFileName("invoice-001.pdf");

        doc.FileName.Should().Be("invoice-001.pdf");
    }

    [Fact]
    public void Create_DefaultContentType_IsPdf()
    {
        var doc = new Document();

        doc.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public void SetContentType_UpdatesValue()
    {
        var doc = new Document();
        doc.SetContentType("image/png");

        doc.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void SetFileSizeBytes_TracksFileSize()
    {
        var doc = new Document();
        doc.SetFileSizeBytes(1024 * 512);

        doc.FileSizeBytes.Should().Be(524288);
    }

    [Fact]
    public void SetStoragePath_AssignsLocation()
    {
        var doc = new Document();
        doc.SetStoragePath("/blobs/invoices/2026/03/invoice-001.pdf");

        doc.StoragePath.Should().Be("/blobs/invoices/2026/03/invoice-001.pdf");
    }

    [Fact]
    public void SetNotes_AssignsDescription()
    {
        var doc = new Document();
        doc.SetNotes("Signed copy from guest");

        doc.Notes.Should().Be("Signed copy from guest");
    }

    [Fact]
    public void PersistenceId_IsGenerated()
    {
        var doc = new Document();

        doc.PersistenceId.Should().NotBeEmpty();
    }
}
