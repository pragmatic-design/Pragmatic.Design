# Examples — pragmatic-use-imaging

Copied from `examples/showcase`, which compiles in the repository and is exercised by
`examples/showcase/tests/Showcase.IntegrationTests (InvoiceQrCodeTests)`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`src/Showcase.Billing/Invoices/Endpoints/InvoiceQrCodeEndpoint.cs`](src/Showcase.Billing/Invoices/Endpoints/InvoiceQrCodeEndpoint.cs) | A QR code as a PNG from an endpoint: `QrCode.GeneratePng`, then `ImagePipeline.Load` with `ImagingOptions.Strict`, `Thumbnail`, `Encode`, returned as a `FileResponse` |
