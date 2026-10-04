---
title: "Troubleshooting"
description: "Runtime errors and their fixes."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Imaging/docs/troubleshooting.md
sidebar:
  order: 6
---
Runtime errors and their fixes.

---

## Deployment

### "Unable to load DLL 'Pragmatic.Imaging.Native'" or `DllNotFoundException`

The native binary isn't findable. In order of likelihood:

1. **Wrong RID / unsupported platform.** The NuGet ships `win-x64`, `linux-x64` (glibc) and `osx-arm64`; any other RID needs a locally-built binary. See [native-deployment.md](/modules/imaging/native-deployment/).
2. **Alpine Linux (musl libc).** The native library requires glibc. Use a Debian/Ubuntu image, or build for musl.
3. **Self-contained publish missing the runtime folder.** Run `dotnet publish -r <rid> --self-contained` and check the native binary landed next to your app.
4. **A custom build step excluded `runtimes/`.** Verify with `ls bin/Release/net10.0/runtimes/`.

### `DllNotFoundException` on first request but not in unit tests

Tests run in the project's output folder where the native binary is present. The published artifact may differ: check the publish output contains `Pragmatic.Imaging.Native.dll` (or `libPragmatic.Imaging.Native.so`).

---

## Runtime: `ImagingException.Reason`

Imaging-pipeline failures throw `ImagingException`; branch on `Reason` (`ImagingError`).

### `Reason == DecodeFailed`

The input isn't a valid image in a supported format, or decoded to zero dimensions. Check:
- `ImageInfo.FromBytes(input)` to see what the header identifies
- the input is complete (truncated downloads decode-fail)

### `Reason == InputTooLarge`

The encoded input exceeded `ImagingOptions.MaxInputBytes` (checked before decode). Relax the limit for legitimate large inputs, or reject.

### `Reason == MaxWidthExceeded` / `MaxHeightExceeded` / `MaxMegapixelsExceeded`

The decoded image violates a safety cap (checked from the header, before full decode). Either relax `ImagingOptions`, or log and reject: this is the decompression-bomb guard working.

### `Reason == FormatNotAllowed`

The decoded format isn't in `ImagingOptions.AllowedFormats`. Add the format to the allow-list, or reject the upload.

### `Reason == EncodeFailed` / `UnsupportedFormat`

Encoding to the requested format failed (e.g. encoding to `ImageFormat.Unknown`). Choose a valid output format.

### `ArgumentException` (not `ImagingException`)

Argument-shape mistakes throw `ArgumentException`:
- `Crop(x, y, w, h)` extends past the image bounds, or a zero dimension
- `Rotate(45)`: only 90/180/270 allowed
- `Resize(0, 0)`: dimensions must be positive
- `QrCode.GeneratePng("")`: empty text, or `moduleSize`/`margin` out of range

The message identifies which argument failed.

### Output file is empty (0 bytes)

- `Encode`/`EncodeTo` write immediately and return the bytes; they don't defer.
- Make sure the output stream isn't closed before `EncodeTo` runs, and is flushed/disposed after.

```csharp
using var pipe = ImagePipeline.Load(bytes);
pipe.Resize(400, 400);
using (var output = File.Create("out.png"))
    pipe.EncodeTo(output, ImageFormat.Png);   // stream disposed after EncodeTo returns
```

### JPEG output looks blocky / low quality

You passed a low `quality`. `50` is visibly degraded for photos; `75` is acceptable; `85` is typical; `95+` is archival.

### WebP or AVIF ignores `quality`

Expected. WebP encodes lossless and AVIF uses the library default in `image` 0.25: `quality` only affects JPEG. For controllable lossy size, encode JPEG.

---

## Memory

### Process RSS keeps growing under load

Check every `ImagePipeline.Load` has a matching `Dispose`. Without disposal, native memory leaks until the .NET finaliser runs, which can be minutes under GC pressure. Look for pipelines stored in fields or captured by long-lived closures without a dispose path.

### `OutOfMemoryException` under high-concurrency load

Too many pipelines alive at once. Cap concurrency with `ImageBatch.ProcessAsync(..., maxConcurrency: 4)` rather than `Task.WhenAll` over hundreds of items. Rule of thumb: one active pipeline holds ~(width × height × 4) bytes native; an 8000×8000 image is ~256 MB.

---

## Performance

### Batch processing slower than sequential

Likely oversubscribed: CPU-bound native work doesn't scale past physical cores. Use `maxConcurrency: Environment.ProcessorCount` (or the default `ProcessorCount / 2`) rather than a large fixed number.

### First call is slow, subsequent calls are fast

The native library loads lazily on first use. Pre-warm at startup if latency matters:

```csharp
// In Program.cs startup: triggers the native load
_ = ImageInfo.FromBytes(QrCode.GeneratePng("warmup", moduleSize: 1, margin: 0));
```

### `Lanczos3` resize is slow for thumbnails

For small outputs, `CatmullRom` is faster with imperceptible difference:

```csharp
pipe.Thumbnail(200, 200, ResizeFilter.CatmullRom);
```

Reserve `Lanczos3` for hero images and large outputs.

---

## Outputs look wrong

### Image is rotated 90°

The source has an EXIF orientation tag (common from phone cameras). The decoder returns raw pixel orientation and re-encoding strips EXIF, so the stored orientation is lost. Read the orientation yourself and apply an explicit `Rotate(...)`; the library doesn't auto-rotate.

### Colours look off

All pixels are assumed sRGB. Wide-gamut sources (P3, AdobeRGB) shift when re-encoded as sRGB. There's no colour-space conversion; transcode to sRGB before the pipeline if needed.

### Transparency becomes black in JPEG output

JPEG has no alpha channel; transparent pixels composite against black. Flatten against a background colour before encoding (not a first-class operation; preprocess elsewhere) or encode to PNG/WebP.

---

## Still stuck?

- The corresponding sample in `samples/Pragmatic.Imaging.Samples/` runs end-to-end and can be diffed against your code.
- File an issue with a minimal reproducer, the RID, and the `ImagingException.Reason` if applicable.
