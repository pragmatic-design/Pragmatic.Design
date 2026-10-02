using System.Reflection;
using Pragmatic.Attachments;

namespace Pragmatic.Attachments.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 1 — [HasAttachments] attribute options.
//
// [HasAttachments] is the trait marker the SG reads off the decorated entity to
// emit the metadata entity, upload/download/delete actions and endpoints. Here
// we declare entities that opt in with different option sets and read the
// options back via reflection — exactly the option surface the generator
// consumes from the symbol model. (Declaring the attribute compiles; the
// generated feature only exists inside a real Pragmatic host.)
// ─────────────────────────────────────────────────────────────────────────────

// Free-form defaults: MaxPerEntity=20, MaxFileSizeBytes=10 MB, all extensions.
[HasAttachments]
internal sealed class Message;

// Contract documents: at most 5 PDFs, 25 MB each, dedicated storage container, and a
// 90-day retention window after which the generated purge job reclaims deleted blobs.
[HasAttachments(
    MaxPerEntity = 5,
    MaxFileSizeBytes = 25 * 1024 * 1024,
    AllowedExtensions = ".pdf",
    Container = "contracts",
    PurgeDeletedAfterDays = 90,
    PurgeCron = "0 4 * * 0")]
internal sealed class Contract;

// Product gallery: unlimited images (MaxPerEntity=0), 2 MB cap, image types only.
[HasAttachments(
    MaxPerEntity = 0,
    MaxFileSizeBytes = 2 * 1024 * 1024,
    AllowedExtensions = ".jpg,.jpeg,.png,.webp",
    SubBoundary = "ProductImages")]
internal sealed class Product;

internal static class HasAttachmentsOptionsSample
{
    public static void Run()
    {
        Console.WriteLine("== Sample 1: [HasAttachments] attribute options ==");
        Console.WriteLine();

        Describe<Message>("free-form (defaults)");
        Describe<Contract>("contract documents (PDF only)");
        Describe<Product>("product gallery (unlimited images)");

        Console.WriteLine();
    }

    private static void Describe<T>(string label)
    {
        var attr = typeof(T).GetCustomAttribute<HasAttachmentsAttribute>();
        if (attr is null)
        {
            Console.WriteLine($"  {typeof(T).Name,-9} : (no [HasAttachments])");
            return;
        }

        // 0 / negative => unlimited (documented contract on the attribute).
        var count = attr.MaxPerEntity <= 0 ? "unlimited" : attr.MaxPerEntity.ToString();
        var size = attr.MaxFileSizeBytes <= 0 ? "no limit" : $"{attr.MaxFileSizeBytes / (1024 * 1024)} MB";
        var ext = string.IsNullOrEmpty(attr.AllowedExtensions) ? "any" : attr.AllowedExtensions;
        // Default container is the parent type name lower-cased.
        var container = attr.Container ?? typeof(T).Name.ToLowerInvariant();
        var subBoundary = attr.SubBoundary ?? $"{typeof(T).Name}Attachments";

        Console.WriteLine($"  {typeof(T).Name,-9} : {label}");
        // 0 / negative => no purge job is generated at all (the default).
        var purge = attr.PurgeDeletedAfterDays <= 0
            ? "off (blobs of soft-deleted attachments are kept forever)"
            : $"after {attr.PurgeDeletedAfterDays} days, cron '{attr.PurgeCron}'";

        Console.WriteLine($"      MaxPerEntity={count}, MaxFileSize={size}, AllowedExtensions={ext}");
        Console.WriteLine($"      Container='{container}', SubBoundary='{subBoundary}'");
        Console.WriteLine($"      Purge: {purge}");
    }
}
