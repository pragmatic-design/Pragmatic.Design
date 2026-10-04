---
title: "Getting Started"
description: "Five concrete scenarios, each ~3 minutes."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Imaging/docs/getting-started.md
sidebar:
  order: 2
---
Five concrete scenarios, each ~3 minutes.

---

## Install

```bash
dotnet add package Pragmatic.Imaging
```

The NuGet includes the native binary for `win-x64`, `linux-x64` (glibc) and `osx-arm64`; the macOS one is built and stamped by the `Imaging Native` CI workflow on a macOS runner, and no test runs it on macOS yet. See [native-deployment.md](/modules/imaging/native-deployment/).

---

## Scenario 1: resize and convert

Load a JPEG, thumbnail it, write WebP.

```csharp
using Pragmatic.Imaging;

using var pipe = ImagePipeline.Load(File.ReadAllBytes("photo.jpg"));

pipe.Thumbnail(maxWidth: 400, maxHeight: 400);
using var output = File.Create("photo-thumb.webp");
pipe.EncodeTo(output, ImageFormat.WebP);
```

`Thumbnail` preserves aspect ratio and fits the longest side within the box. For an exact fixed size, use `Resize(width, height)`.

---

## Scenario 2: apply filters

Grayscale + sharpen + tonal tweaks.

```csharp
using var pipe = ImagePipeline.Load(bytes);

pipe.Grayscale()
    .Sharpen(sigma: 1.5f, threshold: 2)
    .Brightness(10)       // -255..255
    .Contrast(15f);       // additive; 0 = identity

using var output = File.Create("scan.png");
pipe.EncodeTo(output, ImageFormat.Png);
```

See [operations.md](/modules/imaging/operations/) for every filter parameter.

---

## Scenario 3: inspect an upload before decoding

Validate dimensions and format cheaply (header only) before running anything expensive, then hand the same limits to `Load`.

```csharp
using Pragmatic.Imaging;

await using var upload = file.OpenReadStream();   // IFormFile: buffered, seekable
var info = await ImageInfo.FromStreamAsync(upload, maxBytes: 65_536, ct: ct);

if (info.Format is not (ImageFormat.Jpeg or ImageFormat.Png or ImageFormat.WebP))
    return BadRequest("Unsupported format");
if (info.Width > 8000 || info.Height > 8000)
    return BadRequest("Image too large");

upload.Position = 0;   // ImageInfo only read the header

var options = new ImagingOptions
{
    MaxWidth = 8000,
    MaxHeight = 8000,
    MaxMegapixels = 50,
    AllowedFormats = [ImageFormat.Jpeg, ImageFormat.Png, ImageFormat.WebP],
};

using var pipe = ImagePipeline.Load(upload, options);
// ... proceed with processing
```

`ImagePipeline.Load` re-checks the same `ImagingOptions` from the header before decoding, so even if you skip the manual pre-check the limits still hold. `AllowedFormats` is a fail-closed allow-list; a rejected upload throws `ImagingException { Reason = FormatNotAllowed }`.

---

## Scenario 4: batch thumbnailing

`ImageBatch` is static and works on `byte[]` inputs with bounded concurrency.

```csharp
using Pragmatic.Imaging;

var files = Directory.GetFiles("photos", "*.jpg");
var images = await Task.WhenAll(files.Select(f => File.ReadAllBytesAsync(f)));

byte[][] thumbs = await ImageBatch.ThumbnailsAsync(
    images,
    maxWidth: 256, maxHeight: 256,
    format: ImageFormat.WebP,
    quality: 75,
    maxConcurrency: 4);   // 0 → Environment.ProcessorCount / 2

for (var i = 0; i < files.Length; i++)
    await File.WriteAllBytesAsync(Path.ChangeExtension(files[i], ".thumb.webp"), thumbs[i]);
```

`maxConcurrency` caps how many pipelines are alive at once; 4–8 is a safe start on a typical server. For a custom per-item operation, use `ImageBatch.ProcessAsync(images, data => …)`.

---

## Scenario 5: generate a QR code

No pipeline needed: QR generation is a direct static call.

```csharp
using var output = File.Create("qr.png");
QrCode.GeneratePng(
    text: "https://pragmaticdesign.net",
    output: output,
    moduleSize: 10,   // pixels per module (1–1000)
    margin: 2);       // quiet zone in modules (≤100)
```

Output is always PNG, black on white. Thread-safe, no options object.

---

## Beyond the five scenarios

### One-liners

When you don't need a pipeline, `ImageConverter` wraps the common cases:

```csharp
byte[] thumb = await ImageConverter.ThumbnailAsync(bytes, 400, 400, ImageFormat.Jpeg, quality: 90);
byte[] webp  = await ImageConverter.ConvertAsync(bytes, ImageFormat.WebP);
byte[] clean = ImageConverter.StripExif(bytes);   // drop metadata via re-encode
```

### Streaming to an HTTP response

```csharp
[HttpGet("/thumbnail")]
public async Task ThumbnailAsync(CancellationToken ct)
{
    var bytes = await GetImageAsync(ct);
    using var pipe = ImagePipeline.Load(bytes, ImagingOptions.Strict);
    pipe.Thumbnail(400, 400);
    Response.ContentType = "image/webp";
    await pipe.EncodeToStreamAsync(Response.Body, ImageFormat.WebP, ct: ct);
}
```

Always pass `ImagingOptions` (start from `ImagingOptions.Strict`) for untrusted input: the defaults are conservative but not tailored to your workload.

---

## Runnable samples

- [`Pragmatic.Imaging.Samples`](https://github.com/pragmatic-design/Pragmatic.Design/tree/main/Pragmatic.Imaging/samples/Pragmatic.Imaging.Samples): QR / image info / resize / format / filters / crop / rotate / flip / chained transforms / batch

---

## Next

- [Operations reference](/modules/imaging/operations/): full method catalogue with parameters and semantics
- [Native deployment](/modules/imaging/native-deployment/): platforms, AOT, Docker notes
- [Concepts](/modules/imaging/concepts/): architecture, thread safety, error model
