---
title: "Architecture and Core Concepts"
description: "`Pragmatic.Imaging` is a small image pipeline for server-side .NET: a managed engine (`Pragmatic.Imaging.Codecs`) for PNG, JPEG, WebP, BMP and every operation, "
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Imaging/docs/concepts.md
sidebar:
  order: 1
---
`Pragmatic.Imaging` is a small image pipeline for server-side .NET: a managed engine (`Pragmatic.Imaging.Codecs`) for PNG, JPEG, WebP, BMP and every operation, and a Rust native library built on the `image` crate, still shipped, for AVIF, GIF and TIFF until their managed codecs land. It exists to give server-side .NET code a small, AOT-friendly, predictable image pipeline without adopting a heavy managed dependency.

---

## The Problem

Server-side image processing in .NET typically means:

- **ImageSharp / SkiaSharp**: capable, but larger dependency surface and non-trivial AOT / trimming story
- **System.Drawing.Common**: dead end on non-Windows, no modern format support
- **Call out to ImageMagick / FFmpeg**: works but adds a spawned-process failure domain

Pragmatic.Imaging takes a different shape: a small, purposeful API over its own codecs for the formats servers actually care about.

## The approach

- **Managed engine**: `Pragmatic.Imaging.Codecs` decodes and encodes PNG and JPEG, decodes WebP (lossless, lossy with alpha, an animation's first frame) to the pixels libwebp reads and encodes it lossless or, by name, lossy, decodes and encodes BMP as the native library did, and runs every operation (resize, crop, rotate, flip, filters), with vector paths where they pay. Its operations give the native library's results: exactly for geometry, greyscale, brightness and contrast, within one unit per channel for blur and sharpen.
- **Native library, for the rest**: AVIF, GIF and TIFF are decoded and encoded by the Rust library, as is a JPEG the managed decoder does not handle (CMYK, 12-bit, arithmetic-coded, lossless). Nothing else reaches it.
- **Controlled native memory**: decoded pixels live in a `PixelBuffer`, native memory from a pool the garbage collector does not move or count, whichever engine decoded them.

```
    ┌──────────────────────────────────────────────────┐
    │   ImagePipeline                                  │
    │   ┌───────────────────────────────────────────┐  │
    │   │ PixelBuffer (pooled native memory)        │  │
    │   └───────────────────────────────────────────┘  │
    │   Resize → Crop → Grayscale → …  (managed)       │
    └──────────────────────────────────────────────────┘
          │ load / encode, by format
          ├──────────────────────────┐
          ▼                          ▼
    Pragmatic.Imaging.Codecs   Pragmatic.Imaging.Native.dll / .so / .dylib
    PNG, JPEG, WebP, BMP       AVIF, GIF, TIFF
    (managed)                  (LibraryImport P/Invoke)
```

The native library file is named `Pragmatic.Imaging.Native.dll` on Windows and
`libPragmatic.Imaging.Native.so` / `.dylib` on Linux / macOS: the name the P/Invoke asks for. An
application that only reads and writes PNG, JPEG and WebP never loads it.

---

## The pipeline model

Every multi-step imaging operation goes through `ImagePipeline`. You load once, chain transformations, encode.

```csharp
using var pipeline = ImagePipeline.Load(File.ReadAllBytes("input.jpg"));

var png = pipeline
    .Resize(800, 600)
    .Sharpen(sigma: 1.2f)
    .Grayscale()
    .Encode(ImageFormat.Png);
```

Each method returns the same `ImagePipeline` instance: it's an **in-place builder**, not an immutable chain. You have one pixel buffer for the image: the flips, greyscale, brightness and contrast change it in place, and the other operations replace it with a new one and give the old one back to the pool. This keeps native memory use predictable.

`Encode`/`EncodeTo` **do not** dispose the pipeline: you can encode the same pipeline state to several formats, or keep transforming afterwards. The pixel buffer goes back to the pool only when you dispose the pipeline (via `using`); a forgotten pipeline holds it until the finaliser runs, so always use `using` or `await using`.

```csharp
using var pipe = ImagePipeline.Load(bytes);
pipe.Thumbnail(400, 400);
var webp = pipe.Encode(ImageFormat.WebP);   // WebP copy
var png  = pipe.Encode(ImageFormat.Png);    // …and a PNG copy; pipe still valid
```

---

## Formats

### Decoders (read)

PNG, JPEG, WebP, AVIF, GIF (first frame for static), BMP, TIFF.

### Encoders (write)

PNG, JPEG, WebP, AVIF, GIF (first frame), BMP, TIFF: all decodable formats can also be encoded.

The `quality` argument (1–100) of `Encode` applies to **JPEG only**. WebP is written lossless whatever it says; lossy WebP is asked for by name, `EncodeWebP(WebPEncoding.Lossy, quality)`, so a call that names no encoding never changes what it writes. AVIF uses the native library default, which `image` 0.25 does not let a quality change. PNG/BMP/TIFF/GIF are lossless or paletted and ignore `quality` too.

---

## Loading

`ImagePipeline.Load` accepts a span, a `byte[]`, or a `Stream`. It decodes the image immediately:

```csharp
using var pipe = ImagePipeline.Load(byteArray);
using var pipe = ImagePipeline.Load(memory.Span);   // zero-copy span overload
using var pipe = ImagePipeline.Load(fileStream);    // reads the whole stream (bounded by MaxInputBytes)
```

- the format is identified at load time (not lazily)
- invalid input fails fast with `ImagingException { Reason = DecodeFailed }`
- once loaded, transformations don't pay the decode cost again
- a JPEG or PNG with an EXIF orientation is loaded the way it is shown: a portrait phone photo, stored sideways with tag 6, comes out upright, and `Width`/`Height` are the shown size. `ImageInfo.FromBytes` reports that size too, so the limits are checked against the image that will exist.

`LoadAsync(ReadOnlyMemory<byte>, ct, options)` and `LoadAsync(Stream, ct, options)` offload the CPU-bound decode to the thread pool.

### What a call holds in memory

- **Decoded pixels:** width × height × 4 bytes of native memory per live pipeline, given back to the pool on dispose. `NativeBufferPool.RentedBytes` (in `Pragmatic.Imaging.Codecs`) counts every byte rented, for a caller that wants to measure it.
- **Encoded input read from a `Stream`:** `Load(Stream)`, `LoadAsync(Stream)` and `ImageInfo.FromStream` read the encoded bytes into a buffer rented from `ArrayPool<byte>.Shared` and return it once decoded. A seekable stream sizes the buffer from its remaining length; otherwise the buffer starts at 80 KB and doubles, up to `MaxInputBytes` (or `maxBytes` for `FromStream`). So the input is held once, for the duration of the decode, and is not a fresh allocation each call.
- **Encoded output:** `EncodeTo(stream)` and `QrCode.GeneratePng(text, stream)` write PNG and JPEG straight into the stream (the other formats from the native encoder's buffer), without a managed copy. `Encode` and `GeneratePng(text)` return a `byte[]`, which is one copy of the encoded image. `EncodeToStreamAsync` goes through `EncodeAsync`, so it makes that copy too.

### Inspecting without decoding

When you only need dimensions and format, use `ImageInfo`: it parses the header instead of decoding the whole image.

```csharp
ImageInfo info = ImageInfo.FromBytes(bytes);
Console.WriteLine($"{info.Format} {info.Width}×{info.Height}");
```

`ImageInfo` is a `readonly record struct(uint Width, uint Height, ImageFormat Format)`.
`ImageInfo.FromStream(stream, maxBytes: 65536)` reads only enough bytes to identify the header, so you can cheaply validate user uploads without a full decode.

---

## Safety limits

Production image endpoints need to resist pathological inputs: 50,000×50,000 "decompression bombs", crafted headers, unexpected formats.

`ImagingOptions` caps what `Load` will accept. Limits are checked **from the header, before the full decode**:

```csharp
var options = new ImagingOptions
{
    MaxInputBytes  = 20 * 1024 * 1024,   // reject encoded input over 20 MB
    MaxMegapixels  = 25,                 // reject decoded images over 25 MP
    MaxWidth       = 8192,               // 0 = unlimited
    MaxHeight      = 8192,               // 0 = unlimited
    AllowedFormats = [ImageFormat.Jpeg, ImageFormat.Png, ImageFormat.WebP],  // null = any
};

using var pipe = ImagePipeline.Load(bytes, options);
```

| Property | Meaning | Default |
|----------|---------|---------|
| `MaxInputBytes` | Encoded input size cap (bytes) | 100 MB |
| `MaxMegapixels` | Decoded megapixel cap | 100 MP |
| `MaxWidth` / `MaxHeight` | Per-dimension pixel caps (`0` = unlimited) | `0` |
| `AllowedFormats` | Fail-closed format allow-list (`null` = any) | `null` |

Three presets are provided: `ImagingOptions.Default`, `ImagingOptions.Strict` (20 MB / 25 MP, for user uploads), `ImagingOptions.Relaxed` (500 MB / 500 MP, for trusted batch work). The presets leave `MaxWidth`/`MaxHeight`/`AllowedFormats` unset; add them for untrusted input.

Violations throw `ImagingException` with a specific `Reason` (see the error model below).

---

## Encoding

`Encode(format, quality)` returns the encoded bytes; `EncodeTo(stream, format, quality)` writes them to a stream. `EncodeAsync` / `EncodeToStreamAsync` offload the work to the thread pool.

```csharp
byte[] jpeg = pipe.Encode(ImageFormat.Jpeg, quality: 85);
pipe.EncodeTo(outStream, ImageFormat.WebP);
await pipe.EncodeToStreamAsync(response.Body, ImageFormat.Png, ct: ct);
```

Quality applies to JPEG only (see [Formats](#formats)).

---

## Batch processing

`ImageBatch` is a **static** helper for processing many images with bounded concurrency:

```csharp
byte[][] thumbs = await ImageBatch.ThumbnailsAsync(
    images,            // IReadOnlyList<byte[]>
    maxWidth: 256, maxHeight: 256,
    format: ImageFormat.WebP,
    maxConcurrency: 4);   // 0 → Environment.ProcessorCount / 2
```

`ThumbnailsAsync` / `ConvertAsync` cover the common cases; `ProcessAsync(images, operation, maxConcurrency, ct)` runs an arbitrary `Func<ReadOnlyMemory<byte>, byte[]>` per input. A single failed item surfaces as `ImageBatchItemException` (carrying the input `Index`); see [operations.md](/modules/imaging/operations/#batch-processing-imagebatch) for the exact failure semantics.

---

## Thread safety

- `ImagePipeline`: **not thread-safe**. One pipeline per thread/task.
- `ImageInfo.FromBytes`, `QrCode.GeneratePng`, `ImageConverter.*`, `ImageBatch.*`: safe to call concurrently (each opens its own pipeline).

Sharing one `ImagePipeline` across threads corrupts its pixel buffer. The pipeline does not synchronise for you.

---

## Error model

Imaging-pipeline failures surface as `ImagingException`, categorised by `Reason` (`ImagingError`):

```csharp
try
{
    using var pipe = ImagePipeline.Load(bytes, ImagingOptions.Strict);
}
catch (ImagingException ex) when (ex.Reason == ImagingError.MaxMegapixelsExceeded)
{
    // handle decompression-bomb rejection distinctly
}
```

`ImagingError` values: `NativeError`, `DecodeFailed`, `EncodeFailed`, `UnsupportedFormat`, `InputTooLarge`, `MaxWidthExceeded`, `MaxHeightExceeded`, `MaxMegapixelsExceeded`, `FormatNotAllowed`.

**Argument-shape mistakes throw `ArgumentException`, not `ImagingException`**: a zero `Resize` dimension, an out-of-bounds `Crop`, a `Rotate` that isn't 90/180/270, a negative or non-finite blur sigma (`ArgumentOutOfRangeException`), an empty QR string or one too long for a QR code. Those are programming errors, distinct from runtime imaging failures. Encoding to `ImageFormat.Unknown` throws `ImagingException { Reason = UnsupportedFormat }`.

---

## AOT and trimming

The module is AOT-compatible: no reflection, no dynamic code generation, `LibraryImport`-generated P/Invoke for the native formats. `Pragmatic.Imaging.Codecs` ships a `buildTransitive` targets file that compiles its AVX2 paths in under Native AOT (see the module README). The native library ships as a `runtimes/{rid}/native/` asset in the NuGet; the `Pragmatic.Aot.Codecs` smoke publishes PNG, JPEG, the operations and QR codes Native AOT with no native library next to the binary.

See [native-deployment.md](/modules/imaging/native-deployment/) for platform coverage and self-contained publish.

---

## Related

- [getting-started.md](/modules/imaging/getting-started/): minimal tutorials
- [operations.md](/modules/imaging/operations/): full reference of transform / filter methods
- [native-deployment.md](/modules/imaging/native-deployment/): platform matrix and deployment notes
