---
title: "Common Mistakes"
description: "The trait stores metadata, but file bytes still need `Pragmatic.Storage`."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Attachments/docs/common-mistakes.md
sidebar:
  order: 3
---
### 1. Forgetting to configure a storage provider

The trait stores metadata, but file bytes still need `Pragmatic.Storage`.

### 2. Missing `[Entity]`

Without `[Entity]`, the generator cannot infer the parent id type for the FK.

### 3. Missing `[Resource]` and expecting endpoints anyway

The trait can still generate entity and action artifacts, but endpoint generation depends on route metadata from the parent resource.

### 4. Expecting file bytes in the database entity

The generated attachment type stores `StorageUri` and metadata, not the binary payload.

### 5. Using a restrictive extension list and forgetting the leading dot

Prefer `.pdf,.jpg` over `pdf,jpg` to match the package's documented extension format.

### 6. Expecting `DELETE /attachments/{id}` to free the storage

It does not. The generated delete is a soft delete: the metadata row is flagged and the file
stays in `IFileStorage` so the attachment can be restored. Set
`[HasAttachments(PurgeDeletedAfterDays = N)]` to have the blob reclaimed on a schedule; left
at its default (`0`) no job is generated and the bucket grows forever. See
[Retention and purging](/modules/attachments/concepts/#retention-and-purging).

### 7. Reading the bytes from the metadata endpoint

`GET .../attachments/{attachmentId}` returns JSON metadata only, and it does not carry
`StorageUri`. The file comes from `GET .../attachments/{attachmentId}/content`, under the same
`attachments.read` permission.

### 8. Calling `/content` with an attachment id from a different parent

That is a 404, not a download. Every generated attachment action filters on the parent id in
the route as well as the attachment id — the id alone is never sufficient.

