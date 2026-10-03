# Getting Started

## 1. Install the package

```xml
<PackageReference Include="Pragmatic.Attachments" />
```

Typical companion packages:

```xml
<PackageReference Include="Pragmatic.Persistence" />
<PackageReference Include="Pragmatic.Storage" />
<PackageReference Include="Pragmatic.Endpoints" />
```

## 2. Mark the entity

```csharp
[Entity]
[Resource("invoices")]
[HasAttachments(MaxPerEntity = 10, AllowedExtensions = ".pdf,.png")]
public partial class Invoice
{
    public string Number { get; set; } = "";
}
```

## 3. Configure storage

```csharp
await PragmaticApp.RunAsync(args, builder =>
{
    builder.UseStorage(sp => new LocalDiskFileStorage(
        "wwwroot",
        sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));
});
```

## 4. Use the generated feature

After build, the consuming boundary gets attachment actions and endpoints for upload, read (metadata list and detail), download, and delete under the parent resource.

The read side is split in two: `GET .../attachments/{attachmentId}` returns the metadata as JSON, and `GET .../attachments/{attachmentId}/content` streams the file with its recorded content type and file name. Both require the same `attachments.read` permission. The metadata does **not** include `StorageUri`: the content endpoint removes any reason to hand a storage location to the client.

To have soft-deleted attachments' blobs reclaimed on a schedule, set a retention window:

```csharp
[HasAttachments(PurgeDeletedAfterDays = 30)]
```

That generates a `[RecurringJob]` which deletes the blob and then the row. Left unset (the default) nothing is generated and stored files accumulate until you remove them.

