# Architecture and Core Concepts

`Pragmatic.Imaging` is a .NET binding over a Rust native library built on the `image` crate. It exists to give server-side .NET code a small, AOT-friendly, predictable image pipeline without adopting a heavy managed dependency.

---

## The Problem

Server-side image processing in .NET typically means:

- **ImageSharp / SkiaSharp** — capable, but larger dependency surface and non-trivial AOT / trimming story
- **System.Drawing.Common** — dead end on non-Windows, no modern format support
- **Call out to ImageMagick / FFmpeg** — works but adds a spawned-process failure domain

Pragmatic.Imaging takes a different shape: a small, purposeful API over a Rust core that handles decoding, encoding, and transformation for the formats servers actually care about.

## The approach

- **Rust core** — a native library compiled from the `image` crate plus a thin FFI wrapper. Decoding, encoding, and transformations happen native-side.
- **.NET binding** — a small managed API that owns the native buffer, exposes a fluent pipeline, and maps errors to `ImagingException`.
- **Controlled native memory** — decoded pixels live in native memory; the binding deals in pointers and lengths and copies to managed arrays only when it has to.

```
    ┌────────────────────────────────────────────┐
    │   ImagePipeline (managed)                  │
    │   ┌─────────────────────────────────────┐  │
    │   │ native handle → RawImage (unmanaged)│  │
    │   └─────────────────────────────────────┘  │
    │                                            │
    │   Resize → Crop → Grayscale → Encode…      │
    └────────────────────────────────────────────┘
                       │  LibraryImport P/Invoke
                       ▼
    ┌────────────────────────────────────────────┐
    │   Pragmatic.Imaging.Native.dll / .so / .dylib │
    │   ┌─────────────────────────────────────┐  │
    │   │ image crate + FFI shims             │  │
    │   └─────────────────────────────────────┘  │
    └────────────────────────────────────────────┘
```

The native library file is named `Pragmatic.Imaging.Native.dll` on Windows and
`libPragmatic.Imaging.Native.so` / `.dylib` on Linux / macOS: the name the P/Invoke asks for.

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

Each method returns the same `ImagePipeline` instance — it's an **in-place builder**, not an immutable chain. You have one native buffer for the image, and every operation replaces it with the transformed result. This keeps native memory use predictable.

`Encode`/`EncodeTo` **do not** dispose the pipeline: you can encode the same pipeline state to several formats, or keep transforming afterwards. The native buffer is released only when you dispose the pipeline (via `using`) — forgetting to dispose leaks native memory until the finaliser runs, so always use `using` or `await using`.

```csharp
using var pipe = ImagePipeline.Load(bytes);
pipe.Thumbnail(400, 400);
var webp = pipe.Encode(ImageFormat.WebP);   // WebP copy
var png  = pipe.Encode(ImageFormat.Png);    // …and a PNG copy — pipe still valid
```

---

## Formats

### Decoders (read)

PNG, JPEG, WebP, AVIF, GIF (first frame for static), BMP, TIFF.

### Encoders (write)

PNG, JPEG, WebP, AVIF, GIF (first frame), BMP, TIFF — all decodable formats can also be encoded.

The `quality` argument (1–100) applies to **JPEG only**. WebP encodes lossless and AVIF uses the native library default — for both, `quality` is ignored (a limitation of `image` 0.25, which does not expose lossy quality control for these). PNG/BMP/TIFF/GIF are lossless or paletted and ignore `quality` too.

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

`LoadAsync(ReadOnlyMemory<byte>, ct, options)` and `LoadAsync(Stream, ct, options)` offload the CPU-bound decode to the thread pool.

### Inspecting without decoding

When you only need dimensions and format, use `ImageInfo` — it parses the header instead of decoding the whole image.

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

Three presets are provided: `ImagingOptions.Default`, `ImagingOptions.Strict` (20 MB / 25 MP, for user uploads), `ImagingOptions.Relaxed` (500 MB / 500 MP, for trusted batch work). The presets leave `MaxWidth`/`MaxHeight`/`AllowedFormats` unset — add them for untrusted input.

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

`ThumbnailsAsync` / `ConvertAsync` cover the common cases; `ProcessAsync(images, operation, maxConcurrency, ct)` runs an arbitrary `Func<ReadOnlyMemory<byte>, byte[]>` per input. A single failed item surfaces as `ImageBatchItemException` (carrying the input `Index`) — see [operations.md](operations.md#batch-processing) for the exact failure semantics.

---

## Thread safety

- `ImagePipeline` — **not thread-safe**. One pipeline per thread/task.
- `ImageInfo.FromBytes`, `QrCode.GeneratePng`, `ImageConverter.*`, `ImageBatch.*` — safe to call concurrently (each opens its own pipeline).

Sharing one `ImagePipeline` across threads corrupts its native state. The binding does not synchronise for you.

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

**Argument-shape mistakes throw `ArgumentException`, not `ImagingException`** — a zero `Resize` dimension, an out-of-bounds `Crop`, a `Rotate` that isn't 90/180/270, an empty QR string. Those are programming errors, distinct from runtime imaging failures.

---

## AOT and trimming

The binding is AOT-compatible: no reflection, no dynamic code generation, `LibraryImport`-generated P/Invoke. The native library ships as a `runtimes/{rid}/native/` asset in the NuGet.

See [native-deployment.md](native-deployment.md) for platform coverage and self-contained publish.

---

## Related

- [getting-started.md](getting-started.md) — minimal tutorials
- [operations.md](operations.md) — full reference of transform / filter methods
- [native-deployment.md](native-deployment.md) — platform matrix and deployment notes
