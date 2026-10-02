# Pragmatic.Imaging

Image processing for .NET via a Rust native library.

`Pragmatic.Imaging` is designed for AOT-friendly server-side image work: decode, encode, resize, crop, rotate, filter, inspect, and generate QR codes without pulling in large managed imaging stacks.

## Features

- Decode and encode: PNG, JPEG, WebP, AVIF, GIF, BMP, TIFF
- Transform: resize, thumbnail, crop, rotate, flip
- Filters: grayscale, blur, sharpen, brightness, contrast
- QR code generation
- Metadata stripping through re-encoding
- Batch processing with bounded concurrency
- Safety limits through `ImagingOptions`

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

> The `quality` parameter currently applies to **JPEG only**. For WebP and AVIF it is ignored —
> WebP encodes lossless and AVIF uses the native library default.

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

If the native library is missing at runtime, image operations will fail on first use.

## DI

`AddPragmaticImaging()` exists as an extension point for future configuration. The main APIs today are static or fluent and do not require DI.

## Architecture

```text
C# API -> LibraryImport P/Invoke -> Rust native library -> image codecs and transforms
```

Important public entrypoints:

- `ImagePipeline`
- `ImageConverter`
- `ImageBatch`
- `ImageInfo`
- `QrCode`
- `ImagingOptions`

## Operational notes

- **Native buffer lifecycle.** An `ImagePipeline` owns a native image handle. Dispose it
  explicitly (`using var pipeline = ...` or `await using`) — skipping disposal leaks native
  memory the .NET GC cannot reclaim. A pipeline is not thread-safe: do not use one from
  several threads. The static helpers (`ImageConverter`, `ImageBatch`) create and dispose
  their own pipelines.

## Status

**Preview** within 1.0.0-alpha — decoding, encoding, resizing, cropping, rotation, filters and QR codes
run on a native library. The package ships native binaries for `win-x64`, `linux-x64` (glibc) and
`osx-arm64`; the macOS one is built and stamped by the `Imaging Native` CI workflow on a macOS runner,
and no test runs it on macOS yet ([native-deployment.md](docs/native-deployment.md)). See the
[roadmap](../docs/ROADMAP.md).

## Documentation

- [Concepts](docs/concepts.md) — architecture, pipeline model, safety limits, thread safety
- [Getting Started](docs/getting-started.md) — five concrete scenarios
- [Operations Reference](docs/operations.md) — full catalogue of transforms and filters
- [Native Deployment](docs/native-deployment.md) — platforms, AOT, Docker
- [Common Mistakes](docs/common-mistakes.md)
- [Troubleshooting](docs/troubleshooting.md)

Samples:

- [Pragmatic.Imaging.Samples](samples/Pragmatic.Imaging.Samples/README.md)

## Requirements

- .NET 10.0+
- One of the platforms above — the native library is loaded from the package's `runtimes/` folder

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Imaging is licensed under the **PolyForm Small Business 1.0.0** license (free for small businesses; commercial license above the threshold).
