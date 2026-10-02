---
name: pragmatic-use-imaging
description: Use when the app makes thumbnails, converts or resizes uploads, strips metadata, applies filters or renders QR codes — Pragmatic.Imaging, a native AOT-friendly binding, no ImageSharp or SkiaSharp.
---

# Pragmatic Use Imaging

**Covers:** Server-side image processing with Pragmatic.Imaging — decode and encode PNG, JPEG, WebP, AVIF, GIF, BMP, TIFF; resize, thumbnail, crop, rotate; filters, QR codes, EXIF stripping, upload limits. AOT-friendly native binding, no ImageSharp or SkiaSharp.

`Pragmatic.Imaging` is a small managed binding over a Rust native library (built on the `image` crate).
It gives server-side .NET code a predictable, AOT-friendly image pipeline without a heavy managed
dependency. The core APIs are **static or fluent** — no DI required.

## When to use

- Thumbnailing / resizing / format-converting user uploads or stored images.
- Stripping EXIF metadata (privacy) via re-encode.
- Inspecting an upload's dimensions/format cheaply **before** a full decode.
- Applying filters (grayscale, blur, sharpen, brightness, contrast).
- Generating QR code PNGs.

For PDF/DOCX/CSV/XLSX documents use `pragmatic-use-documents` instead — this module is pixels, not pages.

## Package

```bash
dotnet add package Pragmatic.Imaging
```

> **Platform note.** The NuGet ships native binaries for **`win-x64`**, **`linux-x64`** and **`osx-arm64`**
> (the macOS one is built by CI and not yet exercised by a test on macOS); any other RID needs a
> locally-built native library (see the module's `native-deployment.md`). The library requires
> **glibc** on Linux (Alpine/musl is not supported). A missing native binary fails at first use with
> `DllNotFoundException`.

## Core entry points

| Type | Shape | Use for |
|------|-------|---------|
| `ImagePipeline` | fluent, `IDisposable` | multi-step transforms (load → transform → encode) |
| `ImageConverter` | static one-liners | single-op convert/resize/thumbnail/strip-exif |
| `ImageBatch` | **static** | many images with bounded concurrency |
| `ImageInfo` | `readonly record struct` | header-only dimensions/format (no full decode) |
| `QrCode` | static | QR code PNG generation |
| `ImagingOptions` | record | upload safety limits |

## Fluent pipeline

Load once, chain transforms, encode. **Always `using`** — the pipeline owns native memory. `Encode`
does *not* dispose the pipeline, so you can encode several formats from one state.

```csharp
using Pragmatic.Imaging;

using var pipeline = ImagePipeline.Load(imageBytes, ImagingOptions.Strict);

pipeline
    .Thumbnail(1200, 800)          // aspect-preserving, never upscales by default
    .Grayscale()
    .Sharpen(sigma: 1.2f);

byte[] jpeg = pipeline.Encode(ImageFormat.Jpeg, quality: 85);
byte[] webp = pipeline.Encode(ImageFormat.WebP);   // still valid — encode again
```

Transforms: `Resize(w,h,filter)`, `Thumbnail(maxW,maxH,filter,allowUpscale)`, `Crop(x,y,w,h)`,
`Rotate(90|180|270)`, `FlipHorizontal()`, `FlipVertical()`.
Filters: `Grayscale()`, `Blur(sigma)`, `Sharpen(sigma,threshold)`, `Brightness(-255..255)`, `Contrast(-100..100)`.
`ResizeFilter`: `Nearest`, `Triangle`, `CatmullRom`, `Gaussian`, `Lanczos3` (default).

> **`quality` applies to JPEG only.** WebP encodes lossless, AVIF uses the library default, and
> PNG/BMP/TIFF/GIF are lossless/paletted — `quality` is ignored for all of them.

## One-liners

```csharp
byte[] thumb = await ImageConverter.ThumbnailAsync(bytes, maxWidth: 400, maxHeight: 400,
                                                   format: ImageFormat.Jpeg, quality: 90);
byte[] webp  = await ImageConverter.ConvertAsync(bytes, ImageFormat.WebP);
byte[] fixed = ImageConverter.Resize(bytes, 1024, 768);
byte[] clean = ImageConverter.StripExif(bytes);   // drop metadata by re-encoding (lossy for JPEG)
```

## Inspect before decoding (upload validation)

```csharp
await using var upload = file.OpenReadStream();                   // IFormFile: buffered, seekable
var info = await ImageInfo.FromStreamAsync(upload, maxBytes: 65_536, ct: ct);  // header only
if (info.Format is not (ImageFormat.Jpeg or ImageFormat.Png or ImageFormat.WebP))
    return BadRequest("Unsupported format");

upload.Position = 0;   // rewind before Load
```

⚠️ Not on `Request.Body` directly: `FromStream` reads synchronously, which Kestrel refuses by default,
and the raw body cannot be rewound without `Request.EnableBuffering()`.

## Safety limits for untrusted input

Pass `ImagingOptions` whenever the bytes come from a user. Limits are enforced **from the header,
before the full decode**, so decompression bombs are rejected cheaply.

```csharp
using var pipe = ImagePipeline.Load(upload, new ImagingOptions
{
    MaxInputBytes  = 20 * 1024 * 1024,   // encoded-size cap
    MaxMegapixels  = 25,                 // decoded-megapixel cap
    MaxWidth       = 4096,               // per-dimension cap (0 = unlimited)
    MaxHeight      = 4096,
    AllowedFormats = [ImageFormat.Jpeg, ImageFormat.Png, ImageFormat.WebP],  // null = any; fail-closed allow-list
});
```

Presets: `ImagingOptions.Strict` (20 MB / 25 MP, user uploads), `.Default` (100 MB / 100 MP),
`.Relaxed` (500 MB / 500 MP, trusted batch). Add `MaxWidth`/`MaxHeight`/`AllowedFormats` on top for
untrusted input.

## Batch processing

`ImageBatch` is **static** and takes `IReadOnlyList<byte[]>`:

```csharp
byte[][] thumbs = await ImageBatch.ThumbnailsAsync(images, 256, 256, ImageFormat.WebP, maxConcurrency: 4);

byte[][] custom = await ImageBatch.ProcessAsync(images, data =>
{
    using var pipe = ImagePipeline.Load(data.Span);
    pipe.Grayscale().Thumbnail(200, 200);
    return pipe.Encode(ImageFormat.Png);
}, maxConcurrency: 4);   // 0 → Environment.ProcessorCount / 2
```

A failed item throws `ImageBatchItemException` (carrying `Index`); awaiting the batch surfaces the
first failure.

## QR codes

```csharp
byte[] qr = QrCode.GeneratePng("https://example.com/pay/INV-2026-0001", moduleSize: 8, margin: 2);
QrCode.GeneratePng("payload", output: stream);
```

Always PNG, black on white, Medium error-correction. No colour/logo/ECC tuning.

## Error handling

Imaging-pipeline failures throw `ImagingException`; branch on `Reason`:

```csharp
try
{
    using var pipe = ImagePipeline.Load(upload, ImagingOptions.Strict);
}
catch (ImagingException ex) when (ex.Reason is ImagingError.MaxMegapixelsExceeded
                                            or ImagingError.FormatNotAllowed)
{
    return BadRequest("Rejected: " + ex.Message);
}
```

`ImagingError`: `DecodeFailed`, `EncodeFailed`, `UnsupportedFormat`, `InputTooLarge`,
`MaxWidthExceeded`, `MaxHeightExceeded`, `MaxMegapixelsExceeded`, `FormatNotAllowed`, `NativeError`.
**Argument-shape mistakes** (zero dimension, out-of-bounds crop, non-quarter-turn rotate, empty QR
text) throw `ArgumentException`, not `ImagingException`.

## Optional DI

The static/fluent APIs need no DI. `AddPragmaticImaging(options?)` registers an `ImagingOptions`
singleton **and** installs a hardened `NativeLibrary` DLL-import resolver (restricts the native
search path — mitigates DLL hijacking via PATH on Windows). Call it at startup if you want that
hardening even though you use the static APIs.

```csharp
services.AddPragmaticImaging(ImagingOptions.Strict);   // in an IStartupStep, or app.Services in RunAsync
```

## Gotchas

- **Dispose the pipeline** (`using`) — it owns native memory the GC can't reclaim promptly.
- **Not thread-safe**: one `ImagePipeline` per thread/task. `ImageInfo`/`QrCode`/`ImageConverter`/`ImageBatch` are safe (each opens its own pipeline).
- **`Load(Stream)` drains the stream** — rewind if you need to re-read.
- **EXIF is stripped on any re-encode** (this is how `StripExif` works); orientation isn't auto-applied.
  ⚠️ A portrait photo from a phone is stored landscape with an orientation tag: its thumbnail comes out
  **sideways**, and after the re-encode the tag is gone. This library does not read the tag either — read
  it before processing with an EXIF reader of your choice and apply `Rotate(...)`/`Flip*()` yourself, or
  test your upload path with a real phone photo before relying on the thumbnails.
- **sRGB assumed**; no colour-space conversion, no arbitrary-angle rotation, no text/draw, no animation (first frame only).
- **Async methods offload native CPU work to the thread pool**; the `CancellationToken` is checked between operations, not during a native call.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Showcase example application — code
that compiles and that `Showcase.IntegrationTests` exercises — and kept identical to it by the gate: a QR
code generated, loaded, resized and encoded, and returned as a file.

Upload inspection, batch processing and the safety limits beyond `ImagingOptions.Strict` are used by no
tested application yet, so there is no example of them here: the sections above are the reference.
