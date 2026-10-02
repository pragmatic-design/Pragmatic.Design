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
- **win-x64** runtime (Pragmatic.Imaging binds to a Rust native library that
  currently ships only for Windows x64 — see below).

## External dependencies

Pragmatic.Imaging binds to a Rust native library (`pragmatic_imaging_native`).
The NuGet package ships the binaries for **win-x64**, **linux-x64** (glibc) and
**osx-arm64**; any other platform needs a locally-built one (see the module's
docs/native-deployment.md). On an unsupported platform, every sample below throws on its first
Pragmatic.Imaging call — the build itself stays platform-agnostic, only the
runtime is gated.

Output files land under `%TEMP%/pragmatic-imaging-sample/` on Windows
(`/tmp/pragmatic-imaging-sample/` on Linux once the native binding is available).
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
