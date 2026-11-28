# Pragmatic.Imaging

Image processing for .NET: a managed engine for PNG, JPEG, WebP, BMP, GIF, TIFF and every operation, and a Rust native library, still shipped, for AVIF.

`Pragmatic.Imaging` is designed for AOT-friendly server-side image work: decode, encode, resize, crop, rotate, filter, inspect, and generate QR codes without pulling in large managed imaging stacks.

## Features

- Decode and encode: PNG, JPEG, WebP, AVIF, GIF, BMP, TIFF
- Transform: resize, thumbnail, crop, rotate, flip
- Filters: grayscale, blur, sharpen, brightness, contrast
- QR code generation
- EXIF orientation applied on load: a portrait phone photo comes out upright
- Metadata stripping without re-encoding for JPEG and PNG
- Batch processing with bounded concurrency
- Safety limits through `ImagingOptions`, checked from the header before any pixel is allocated

### Which engine does what

| | Managed (`Pragmatic.Imaging.Codecs`) | Native library |
|---|---|---|
| PNG, JPEG: header, decode, encode | yes | a JPEG the managed decoder does not handle: CMYK, 12-bit, arithmetic-coded, lossless |
| WebP: header, decode (lossless, lossy, alpha, an animation's first frame) | yes | |
| WebP: encode, lossless, and lossy with `EncodeWebP` | yes | |
| BMP: header, decode (1 to 8-bit palettes, RLE8, RLE4, 16, 24 and 32-bit, bitfields), encode (32-bit with alpha) | yes | |
| GIF: header, decode (the first frame on its logical screen), encode (an exact table up to 256 colours, Wu's palette over more) | yes | |
| TIFF: header, decode (the first image: strips and tiles, chunky and planar; uncompressed, PackBits, LZW, Deflate, JPEG, Group 4; 1, 8, 16-bit grey, 8 and 16-bit RGB and RGBA, 32-bit float RGB and RGBA, CMYK), encode (8 or 16-bit RGBA, uncompressed) | yes | |
| AVIF: header, decode, encode | | yes, until its managed codec lands (#854) |
| Resize, thumbnail, crop, rotate, flip, filters | yes, whatever format was loaded | |
| QR codes, `StripExif` for JPEG and PNG | yes | |

The choice is made per file, from its signature and (for JPEG) its frame header; a caller sees one API.
The operations give the native library's results: exactly for the geometric operations, greyscale,
brightness and contrast, within one unit per channel for blur and sharpen. Resizing differs on purpose
on a translucent image, which is resampled premultiplied so its edges keep their colour (the native
library darkens them); an opaque one resizes as the native library resized it.

## Installation

```bash
dotnet add package Pragmatic.Imaging
```

## Quick Start

### Fluent pipeline

```csharp
using Pragmatic.Imaging;

using var pipeline = ImagePipeline.Load(imageBytes, ImagingOptions.Strict);

var jpeg = pipeline
    .Thumbnail(1200, 800)
    .Grayscale()
    .Encode(ImageFormat.Jpeg, quality: 85);
```

> The `quality` parameter of `Encode` applies to **JPEG only**. For WebP and AVIF it is ignored:
> WebP encodes lossless and AVIF uses the native library default. PNG is written at zlib's fastest
> level, which on the benchmark's 2 MP round trip is both smaller and faster than the native encoder was.

### Lossy WebP

Lossy WebP is asked for by name, so a call that names no encoding never changes what it writes:

```csharp
var webp = pipeline.EncodeWebP(WebPEncoding.Lossy, quality: 75);
```

`quality` is libwebp's scale, 0 to 100. The file is what libwebp writes at the effort SkiaSharp runs it
at (method 3): on the reference photographs at 50, 75 and 90, SSIM within 0.001 of libwebp's and files
within 0.2% of its size. An image with transparent pixels keeps its alpha exactly beside the frame.

### One-liner helpers

```csharp
var thumb = await ImageConverter.ThumbnailAsync(
    imageBytes,
    maxWidth: 400,
    maxHeight: 400,
    format: ImageFormat.Jpeg,
    quality: 90);

var info = ImageInfo.FromBytes(imageBytes);
var qr = QrCode.GeneratePng("https://www.pragmaticdesign.net");
```

### Batch processing

```csharp
var converted = await ImageBatch.ConvertAsync(
    images,
    ImageFormat.WebP,
    quality: 80,
    maxConcurrency: 4);
```

## Safety and Limits

Use `ImagingOptions` to protect upload and processing paths:

- `ImagingOptions.Default`
- `ImagingOptions.Strict`
- `ImagingOptions.Relaxed`

These limits guard against oversized inputs and decompression-bomb style payloads by capping input bytes and decoded megapixels.

## Runtime Notes

| Platform | Package artifact |
|----------|------------------|
| Windows x64 | shipped in the package |
| Linux x64 (glibc) | shipped in the package |
| macOS arm64 | shipped in the package (built by the `Imaging Native` CI workflow; not yet exercised by a test on macOS) |

The native library serves only what the table above gives it. If it is missing at runtime, loading or
encoding one of those fails on first use; PNG, JPEG, WebP, every operation and QR codes work without
it.

## DI

`AddPragmaticImaging()` exists as an extension point for future configuration. The main APIs today are static or fluent and do not require DI.

## Architecture

```text
C# API -> Engine/ (routes by format)
            ├── PNG, JPEG, WebP, BMP, GIF, TIFF, operations, QR -> Pragmatic.Imaging.Codecs (managed)
            └── AVIF -> LibraryImport P/Invoke -> Rust native library
```

Whatever decoded a file, its pixels sit in a `PixelBuffer` (native memory from a pool, not the
managed heap) and every operation after the load runs managed.

### Pixel formats and depth

A `PixelBuffer` has a `PixelFormat`: `Rgba8` (the default), `Rgba16`, `RgbaFloat`, `Rgb8`, `Rgb16`,
`Gray8` or `Gray16`. A file decodes at its own depth and keeps it to the end:

- a 16-bit PNG or TIFF decodes to `Rgba16`, `Rgb16` or `Gray16` (RGBA when the file has alpha or a
  transparent colour key), a float TIFF to `RgbaFloat`; every file of 8 bits or fewer decodes to `Rgba8`;
- every operation takes every format and returns the format it was given;
- PNG and TIFF write a deep buffer at 16 bits; JPEG, WebP, BMP, GIF and AVIF, which have no deeper form
  here, narrow it to 8 bits at the encode, each 16-bit sample to `round(v × 255 / 65535)`, the native
  library's conversion.

So a 16-bit PNG cropped and saved as PNG comes out with every sample it had.
`PixelConversion.ToRgba8` and `ToRgba16` convert a buffer when code of your own needs one layout.

Important public entrypoints:

- `ImagePipeline`
- `ImageConverter`
- `ImageBatch`
- `ImageInfo`
- `QrCode`
- `ImagingOptions`

## The managed codecs (`Pragmatic.Imaging.Codecs`)

A second package in this module holds the managed engine that replaces the native library, piece by
piece (#821): JPEG decode (baseline, progressive, scaled 1/2 to 1/8) and encode (optimised or standard
Huffman tables), PNG decode and encode (every colour type, 1–16 bits, Adam7), WebP decode (lossless
VP8L, lossy VP8 with its alpha plane, the extended container and an animation's first frame, to the
pixels libwebp reads), lossless encode (VP8L, a colour table for 256 colours or fewer) and lossy encode
(VP8 with segments, rate-distortion mode choice and the alpha plane lossless beside it), resampling
with five
filters, and the QR, Code 128, Code 39 and EAN symbologies as matrices and bar widths. It is managed
code only, with AVX2 paths where they pay.

It also holds the pixel operations (`PixelGeometry`, `PixelAdjustments`, `GaussianBlur`) and the
EXIF tools (`ExifOrientation`, `MetadataStripper`), which the PDF engine needs for the images it embeds
as much as imaging does.

It is the **low-level layer** under `Pragmatic.Imaging` and `Pragmatic.Documents.Pdf`: its types are
public because two packages use it, and an application normally reaches it through those.
`Pragmatic.Imaging` runs on it for PNG, JPEG, WebP and every operation.

**Native AOT.** The package ships a `buildTransitive` targets file that sets
`IlcInstructionSet=x86-64-v3` (AVX2, FMA, BMI: every x64 processor since 2013) when `PublishAot` is on
and the consumer has not chosen an instruction set. Without it, ILC compiles for x86-64-v2, every AVX2
path drops out at compile time and `Vector<T>` is 128 bits wide: the spike measured the same upload
path at 32.6 ms on v2 and 27.4 ms on v3 (one run each, 2026-10-17). To target older processors, set
`IlcInstructionSet` in your project: the package keeps your choice.

## Operational notes

- **Pixel buffer lifecycle.** An `ImagePipeline` owns its pixels in native memory. Dispose it
  explicitly (`using var pipeline = ...` or `await using`): disposal gives the memory back to the
  pool at once, where a forgotten pipeline waits for its finalizer. A pipeline is not thread-safe:
  do not use one from several threads. The static helpers (`ImageConverter`, `ImageBatch`) create
  and dispose their own pipelines.
- **EXIF orientation.** `ImagePipeline.Load` and `ImageInfo.FromBytes` apply a JPEG's or PNG's
  orientation tag: the image is the size and way up it is shown. Do not rotate by the tag yourself.
- **`StripExif`** removes EXIF and XMP from a JPEG or PNG by copying the file without them: no decode,
  no quality loss. The orientation is kept, alone in a new EXIF block. Other formats are re-encoded.

## Status

**Preview** within 1.0.0-alpha: PNG, JPEG, WebP, BMP, GIF and TIFF, every operation and QR codes run on the managed
engine; AVIF still runs on the native library. The package ships native binaries for
`win-x64`, `linux-x64` (glibc) and `osx-arm64`; the macOS one is built and stamped by the `Imaging
Native` CI workflow on a macOS runner, and no test runs it on macOS yet
([native-deployment.md](docs/native-deployment.md)). See the [roadmap](../docs/ROADMAP.md).

## Documentation

- [Concepts](docs/concepts.md): architecture, pipeline model, safety limits, thread safety
- [Getting Started](docs/getting-started.md): five concrete scenarios
- [Operations Reference](docs/operations.md): full catalogue of transforms and filters
- [Native Deployment](docs/native-deployment.md): platforms, AOT, Docker
- [Common Mistakes](docs/common-mistakes.md)
- [Troubleshooting](docs/troubleshooting.md)

Samples:

- [Pragmatic.Imaging.Samples](samples/Pragmatic.Imaging.Samples/README.md)

## Requirements

- .NET 10.0+
- For AVIF, one of the platforms above: the native library is loaded from the
  package's `runtimes/` folder. PNG, JPEG, WebP, BMP, GIF, TIFF, the operations and QR codes run anywhere .NET does.

## License

Part of the [Pragmatic.Design](../README.md) ecosystem. See [Licensing](../docs/LICENSING.md).
Pragmatic.Imaging is licensed under the **PolyForm Small Business 1.0.0** license (free for small businesses; commercial license above the threshold).
