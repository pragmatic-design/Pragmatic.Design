# Pragmatic.Imaging.Samples

Runnable samples for [Pragmatic.Imaging](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Imaging/samples/Pragmatic.Imaging.Samples/Pragmatic.Imaging.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- For the WebP, AVIF, GIF, BMP and TIFF outputs, a platform the native library ships for (see below).

## External dependencies

PNG, JPEG, every operation and QR codes run on the managed engine and need nothing else. WebP, AVIF,
GIF, BMP and TIFF still go through a Rust native library (`pragmatic_imaging_native`); the NuGet
package ships it for **win-x64**, **linux-x64** (glibc) and **osx-arm64**, and any other platform
needs a locally-built one (see the module's docs/native-deployment.md). On an unsupported platform
the samples that write those formats throw on their first call to them.

Output files land under `%TEMP%/pragmatic-imaging-sample/` on Windows
(`/tmp/pragmatic-imaging-sample/` on Linux).
The path is printed on startup. 16 output files are produced:
QR codes, info probes, resized/reformatted images, filtered variants, and
cropped/rotated/flipped transforms.

## Scenarios

- `CropRotateSample`
- `FiltersSample`
- `ImageInfoSample`
- `QrCodeSample`
- `ResizeAndFormatSample`

## Related

- Module: [Pragmatic.Imaging](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/imaging/
- Source: `Pragmatic.Imaging/src/Pragmatic.Imaging/`
