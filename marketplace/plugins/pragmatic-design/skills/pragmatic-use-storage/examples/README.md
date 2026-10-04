# Examples: pragmatic-use-storage

Copied from `examples`, which compiles in the repository and is exercised by
`examples/casework/tests/Casework.IntegrationTests` and `examples/invoicing/tests/Invoicing.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`casework/src/Casework.Intake/Cases/Actions/UploadCaseDocumentAction.cs`](casework/src/Casework.Intake/Cases/Actions/UploadCaseDocumentAction.cs) | An upload: `[MaxFileSize]` and `[AllowedContentTypes]` declared on the `IFormFile`, the bytes saved with `IFileStorage.SaveAsync` in a per-tenant container, the facts about them on the row |
| [`casework/src/Casework.Intake/Cases/Endpoints/DownloadCaseDocumentEndpoint.cs`](casework/src/Casework.Intake/Cases/Endpoints/DownloadCaseDocumentEndpoint.cs) | The download: two filtered reads, then `IFileStorage.GetAsync` into a `FileResponse` with the hash as ETag; 404, never 403 |
| [`invoicing/src/Invoicing.Host/Program.cs`](invoicing/src/Invoicing.Host/Program.cs) | `UseStorage` with a local-disk factory, the one line that changes for Azure or S3 |
| [`invoicing/tests/Invoicing.IntegrationTests/Infrastructure/InvoicingWebFactory.cs`](invoicing/tests/Invoicing.IntegrationTests/Infrastructure/InvoicingWebFactory.cs) | Testing with `InMemoryFileStorage` in place of the disk, which can also count what was stored |
