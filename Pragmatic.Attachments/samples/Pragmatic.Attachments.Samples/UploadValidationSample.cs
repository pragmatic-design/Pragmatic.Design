using System.Reflection;
using Pragmatic.Attachments;

namespace Pragmatic.Attachments.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 3 — Upload validation constraints described via the attribute options.
//
// The SG-generated upload action enforces three rules at runtime BEFORE writing
// to IFileStorage, using exactly the options on [HasAttachments]:
//   • MaxFileSizeBytes  — reject files larger than the cap (0/neg => no limit)
//   • MaxPerEntity      — reject once the entity already holds the max (0/neg => unlimited)
//   • AllowedExtensions — comma-separated; each entry trimmed; case-insensitive;
//                         leading dot optional ("pdf" == ".pdf"); empty => allow all.
//
// The upload endpoint itself IS generated, as a multipart/form-data endpoint that
// binds IFormFile. This sample re-implements that validation contract and
// runs it against the real Contract entity's [HasAttachments] options, so the
// behavior the generator emits is observable without a host.
// ─────────────────────────────────────────────────────────────────────────────

internal static class UploadValidationSample
{
    public static void Run()
    {
        Console.WriteLine("== Sample 3: upload validation contract ==");
        Console.WriteLine();

        // Read the real options off the Contract entity from Sample 1.
        var opts = typeof(Contract).GetCustomAttribute<HasAttachmentsAttribute>()!;
        Console.WriteLine($"  Contract limits: MaxPerEntity={opts.MaxPerEntity}, " +
                          $"MaxFileSize={opts.MaxFileSizeBytes / (1024 * 1024)} MB, " +
                          $"AllowedExtensions='{opts.AllowedExtensions}'");
        Console.WriteLine();

        // currentCount = how many attachments the entity already has.
        Check(opts, currentCount: 0, "service-agreement.pdf", 5L * 1024 * 1024);   // ok
        Check(opts, currentCount: 0, "SCAN.PDF", 1L * 1024 * 1024);                 // ok (case-insensitive)
        Check(opts, currentCount: 0, "photo.png", 1L * 1024 * 1024);                // wrong extension
        Check(opts, currentCount: 0, "huge.pdf", 30L * 1024 * 1024);                // too large
        Check(opts, currentCount: 5, "another.pdf", 1L * 1024 * 1024);             // limit reached

        Console.WriteLine();
    }

    private static void Check(HasAttachmentsAttribute opts, int currentCount, string fileName, long size)
    {
        var reason = Validate(opts, currentCount, fileName, size);
        var verdict = reason is null ? "ACCEPT" : $"REJECT ({reason})";
        Console.WriteLine($"  {fileName,-22} {size / (1024 * 1024),3} MB, have {currentCount} -> {verdict}");
    }

    // Mirrors the SG-generated upload action's pre-storage validation, using the
    // exact parsing contract documented on HasAttachmentsAttribute.
    private static string? Validate(HasAttachmentsAttribute opts, int currentCount, string fileName, long size)
    {
        if (opts.MaxPerEntity > 0 && currentCount >= opts.MaxPerEntity)
            return $"max {opts.MaxPerEntity} attachments reached";

        if (opts.MaxFileSizeBytes > 0 && size > opts.MaxFileSizeBytes)
            return "file exceeds size limit";

        if (!IsExtensionAllowed(opts.AllowedExtensions, fileName))
            return "extension not allowed";

        return null;
    }

    private static bool IsExtensionAllowed(string allowedExtensions, string fileName)
    {
        if (string.IsNullOrEmpty(allowedExtensions))
            return true; // empty => allow all

        var fileExt = Normalize(Path.GetExtension(fileName)); // includes the leading dot

        foreach (var raw in allowedExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Normalize(raw) == fileExt)
                return true;
        }

        return false;
    }

    // Trim, lower-case, ensure a single leading dot (leading dot is optional in config).
    private static string Normalize(string ext)
    {
        ext = ext.Trim().ToLowerInvariant();
        if (ext.Length == 0)
            return ext;
        return ext[0] == '.' ? ext : "." + ext;
    }
}
