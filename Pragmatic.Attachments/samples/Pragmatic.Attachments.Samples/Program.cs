using Pragmatic.Attachments.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Pragmatic.Attachments — runnable samples.
//
// Pragmatic.Attachments is a TRAIT package. Its [HasAttachments] SOURCE
// GENERATOR IS implemented: in a real Pragmatic host it emits the typed
// {Parent}Attachment metadata entity, an EF Core configuration, the
// upload/download/delete actions and the REST endpoints for every entity
// decorated with [HasAttachments]. A plain console app does NOT run that host
// SG pipeline, so the samples below exercise only the runtime surface that runs
// WITHOUT the generator:
//   1. HasAttachmentsAttribute         — options / defaults
//   2. AttachmentBase<Guid> subclass   — file-metadata entity shape + soft delete
//   3. upload validation contract      — described via the attribute options
//
// NB: the SG DOES generate a multipart upload endpoint; what a console sample cannot
// show is the generation itself. File content flows through
// Pragmatic.Storage.IFileStorage, not a generated request DTO.
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("Pragmatic.Attachments Samples");
Console.WriteLine("=============================");
Console.WriteLine("(The [HasAttachments] SG runs in a real Pragmatic host; this");
Console.WriteLine(" console app demos only the runtime types that execute without it.)");
Console.WriteLine();

HasAttachmentsOptionsSample.Run();
AttachmentEntitySample.Run();
UploadValidationSample.Run();

Console.WriteLine("All samples completed.");
