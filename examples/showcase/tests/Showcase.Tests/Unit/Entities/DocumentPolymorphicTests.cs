using Pragmatic.Testing.Assertions;
using Showcase.Billing.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests that [PolymorphicAttachment] on Document generates OwnerType/OwnerId + ForOwner extensions.
/// </summary>
public class DocumentPolymorphicTests
{
    [Fact]
    public void Document_HasOwnerTypeProperty()
    {
        var doc = new Document();
        typeof(Document).GetProperty("OwnerType").Should().NotBeNull();
        doc.OwnerType.Should().Be(""); // default empty
    }

    [Fact]
    public void Document_HasOwnerIdProperty()
    {
        var doc = new Document();
        typeof(Document).GetProperty("OwnerId").Should().NotBeNull();
        doc.OwnerId.Should().Be(""); // default empty
    }

    [Fact]
    public void DocumentAttachmentExtensions_HasForOwnerGeneric()
    {
        var type = typeof(Document).Assembly.GetType("Showcase.Billing.Entities.DocumentAttachmentExtensions");
        type.Should().NotBeNull("SG should generate DocumentAttachmentExtensions");

        var method = type!.GetMethod("ForOwner");
        method.Should().NotBeNull("ForOwner<T> should exist");
        method!.IsGenericMethod.Should().BeTrue();
    }

    [Fact]
    public void DocumentAttachmentExtensions_HasForInvoice()
    {
        var type = typeof(Document).Assembly.GetType("Showcase.Billing.Entities.DocumentAttachmentExtensions");
        type.Should().NotBeNull();

        var method = type!.GetMethod("ForInvoice");
        method.Should().NotBeNull("ForInvoice typed extension should exist");
    }

    [Fact]
    public void DocumentAttachmentExtensions_HasForReservation()
    {
        var type = typeof(Document).Assembly.GetType("Showcase.Billing.Entities.DocumentAttachmentExtensions");
        type.Should().NotBeNull();

        var method = type!.GetMethod("ForReservation");
        method.Should().NotBeNull("ForReservation typed extension should exist");
    }
}
