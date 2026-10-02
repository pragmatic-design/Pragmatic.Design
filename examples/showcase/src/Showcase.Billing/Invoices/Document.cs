using Showcase.Booking.Entities;

namespace Showcase.Billing.Entities;

/// <summary>
/// A document that can be attached to different entity types (Invoice, Reservation).
/// Demonstrates [PolymorphicAttachment] pattern with OwnerType/OwnerId columns.
/// </summary>
[Entity]
[Auditable]
[PolymorphicAttachment]
[Attachable<Invoice>]
[Attachable<Reservation>]
public partial class Document : IEntity
{
    [Required]
    public string FileName { get; private set; } = "";

    public string ContentType { get; private set; } = "application/pdf";

    public long FileSizeBytes { get; private set; }

    public string? StoragePath { get; private set; }

    /// <summary>Human-readable description or notes about the document.</summary>
    public string? Notes { get; private set; }

}
