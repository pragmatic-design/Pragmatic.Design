using Pragmatic.Attachments;

namespace Pragmatic.Attachments.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 2 — Concrete AttachmentBase<TEntityId> metadata entity.
//
// In a real host the SG emits "{Parent}Attachment : AttachmentBase<TEntityId>"
// per decorated entity. Here we HAND-WRITE the same shape (the SG output is not
// present in a bare console app) to show the base type that exists today: the
// Id <-> PersistenceId forwarding, the file-metadata columns (name, size,
// content type, storage URI) and the ISoftDelete columns. Note that only the
// METADATA lives in this entity — the actual bytes live in IFileStorage, keyed
// by StorageUri.
// ─────────────────────────────────────────────────────────────────────────────

// Mirrors the SG-generated metadata entity for a Contract (Guid PK).
internal sealed class ContractAttachment : AttachmentBase<Guid>;

internal static class AttachmentEntitySample
{
    public static void Run()
    {
        Console.WriteLine("== Sample 2: AttachmentBase<Guid> metadata entity ==");
        Console.WriteLine();

        var contractId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var att = new ContractAttachment
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ParentEntityId = contractId,
            FileName = "service-agreement.pdf",
            FileSize = 348_120,
            ContentType = "application/pdf",
            // Set by IFileStorage.SaveAsync — never constructed by hand at runtime.
            StorageUri = "contracts/2026/05/11111111-service-agreement.pdf",
            Description = "Signed service agreement, rev. 3",
            UploadedBy = "user-42",
            UploadedAt = DateTimeOffset.UnixEpoch,
        };

        Console.WriteLine("  AttachmentBase<Guid> (ContractAttachment):");
        Console.WriteLine($"      Id            = {att.Id}");
        // PersistenceId forwards to Id — the IEntity contract.
        Console.WriteLine($"      PersistenceId = {att.PersistenceId}   (forwards to Id)");
        Console.WriteLine($"      ParentEntityId= {att.ParentEntityId}");
        Console.WriteLine($"      FileName      = '{att.FileName}'  ({att.FileSize:N0} bytes, {att.ContentType})");
        Console.WriteLine($"      StorageUri    = '{att.StorageUri}'  (resolved via IFileStorage)");
        Console.WriteLine($"      Description   = '{att.Description}'");
        Console.WriteLine($"      UploadedBy    = {att.UploadedBy}  at {att.UploadedAt:u}");

        // Setting PersistenceId writes straight through to Id.
        var rerouted = Guid.Parse("22222222-2222-2222-2222-222222222222");
        att.PersistenceId = rerouted;
        Console.WriteLine($"      after PersistenceId = {rerouted:D}: Id == {att.Id}  -> {att.Id == rerouted}");
        Console.WriteLine();

        // ISoftDelete columns — what the SG-generated Delete action sets.
        att.IsDeleted = true;
        att.DeletedAt = DateTimeOffset.UnixEpoch.AddDays(1);
        att.DeletedBy = "user-42";
        Console.WriteLine("  Soft delete (ISoftDelete):");
        Console.WriteLine($"      IsDeleted={att.IsDeleted}, DeletedAt={att.DeletedAt:u}, DeletedBy={att.DeletedBy}");
        Console.WriteLine("      (the row stays; the file in IFileStorage is removed separately)");
        Console.WriteLine();
    }
}
