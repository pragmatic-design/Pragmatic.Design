---
title: "Operations Reference"
description: "Complete catalogue of transformations and filters on `ImagePipeline`, plus the static helpers. Pipeline transforms modify the image in place and return `this` f"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Imaging/docs/operations.md
sidebar:
  order: 4
---
Complete catalogue of transformations and filters on `ImagePipeline`, plus the static helpers. Pipeline transforms modify the image in place and return `this` for chaining.

---

## Transformations

### `Resize(width, height, filter)`

Resize to exact dimensions. Does **not** preserve aspect ratio: if the ratio differs from the source, the image is stretched. `filter` defaults to `ResizeFilter.Lanczos3`.

```csharp
pipe.Resize(800, 600, ResizeFilter.Lanczos3);
```

`ResizeFilter` values, in order of quality/cost:

| Filter | When to use |
|--------|-------------|
| `Nearest` | Pixel art, speed-critical paths; fastest |
| `Triangle` | Linear; quick downscale, acceptable quality |
| `CatmullRom` | Cubic; good general-purpose, moderate cost |
| `Gaussian` | Soft result, useful for thumbnails |
| `Lanczos3` | Highest-quality (default), slowest |

Passing `0` for either dimension throws `ArgumentException`.

### `Thumbnail(maxWidth, maxHeight, filter, allowUpscale)`

Fit the image inside the bounding box while **preserving aspect ratio**. At most one dimension hits the bound; the other is smaller. By default it never upscales: a source already smaller than the box is returned at its original size. Pass `allowUpscale: true` to enlarge small sources up to the box.

```csharp
pipe.Thumbnail(400, 400);                              // 800×600 → 400×300
pipe.Thumbnail(400, 400, ResizeFilter.CatmullRom);     // faster than Lanczos3
pipe.Thumbnail(400, 400, allowUpscale: true);          // 200×150 → 400×300
```

### `Crop(x, y, width, height)`

Cut a rectangular region. Coordinates are pixels from the top-left corner.

```csharp
pipe.Crop(x: 100, y: 100, width: 400, height: 300);
```

A zero dimension, or a region extending past the image bounds, throws `ArgumentException`.

### `Rotate(degrees)`

Rotate by 90, 180, or 270 degrees (positive = clockwise). Arbitrary angles are **not supported**: the native layer only does lossless quarter-turn rotations.

```csharp
pipe.Rotate(90);    // landscape → portrait
pipe.Rotate(270);   // equivalent to Rotate(-90)
```

Any other value throws `ArgumentException`.

### `FlipHorizontal()` / `FlipVertical()`

Mirror the image along the corresponding axis.

```csharp
pipe.FlipHorizontal();   // left-right mirror
pipe.FlipVertical();     // top-bottom mirror
```

---

## Filters

### `Grayscale()`

Convert to luminance (ITU-R BT.601). The pixel layout stays RGBA (pixels just have R=G=B); the alpha channel is preserved.

```csharp
pipe.Grayscale();
```

### `Blur(sigma)`

Gaussian blur. `sigma` is the standard deviation in pixels; `1.0` is light, `5.0` is heavy.

```csharp
pipe.Blur(sigma: 2.0f);
```

### `Sharpen(sigma, threshold)`

Unsharp-mask sharpening. `sigma` (default `1.0`) controls the underlying blur radius; `threshold` (default `1`) is the minimum channel difference that triggers sharpening.

```csharp
pipe.Sharpen();                              // sigma 1.0, threshold 1
pipe.Sharpen(sigma: 1.5f, threshold: 3);     // stronger, ignores micro-noise
```

### `Brightness(value)`

Add a constant to every channel. Range `-255` to `+255`; values are clamped native-side.

```csharp
pipe.Brightness(20);       // brighten
pipe.Brightness(-30);      // darken
```

### `Contrast(value)`

Additive contrast adjustment. `0` is identity; positive values increase contrast, negative flatten. Typical range `-100` to `+100`.

```csharp
pipe.Contrast(20f);        // more contrast
pipe.Contrast(-15f);       // flatter
```

---

## Encoding

### `Encode(format, quality)` / `EncodeTo(stream, format, quality)`

`Encode` returns the encoded `byte[]`; `EncodeTo` writes to a stream. `format` defaults to `ImageFormat.Png`, `quality` (a `byte`, 1–100) defaults to `90`.

```csharp
byte[] png  = pipe.Encode();                                // PNG
byte[] jpeg = pipe.Encode(ImageFormat.Jpeg, quality: 85);
pipe.EncodeTo(output, ImageFormat.WebP);
```

`quality` applies to **JPEG only**. WebP encodes lossless, AVIF uses the library default, and lossless/paletted formats ignore it. Supported output formats: PNG, JPEG, WebP, AVIF, GIF (first frame), BMP, TIFF.

Encoding **does not** dispose the pipeline: encode multiple times or keep transforming. The async variants `EncodeAsync` / `EncodeToStreamAsync` offload to the thread pool.

---

## Inspection

### `ImageInfo.FromBytes(bytes)` / `FromStream(stream, maxBytes)` / `FromStreamAsync(...)`

Parse the header to read dimensions and format without a full decode.

```csharp
public readonly record struct ImageInfo(uint Width, uint Height, ImageFormat Format);
```

`FromStream` reads at most `maxBytes` (default 64 KB), enough for any header, and does **not** drain the stream. Use it to validate uploads before committing to a full `ImagePipeline.Load`; rewind the stream (`stream.Position = 0`) before loading.

---

## QR codes

### `QrCode.GeneratePng(text, moduleSize, margin)` / `GeneratePng(text, output, moduleSize, margin)`

Generate a PNG-encoded QR code as `byte[]`, or write it to a stream. `GeneratePngAsync` offloads to the thread pool.

| Parameter | Default | Meaning |
|-----------|---------|---------|
| `text` | n/a | Payload: URL, text, any UTF-8 string (non-empty) |
| `moduleSize` | `10` | Pixels per QR module (1–1000) |
| `margin` | `2` | Quiet-zone size in modules (≤100) |

```csharp
byte[] qr = QrCode.GeneratePng("https://pragmaticdesign.net");
QrCode.GeneratePng("payload", output: fileStream, moduleSize: 8, margin: 2);
```

Error-correction level is fixed at Medium. Output is always PNG, black on white: no colour, logo, or ECC tuning. For richer QR output, use a dedicated library.

---

## Combining operations

```csharp
// Thumbnail for a gallery
pipe.Thumbnail(400, 400, ResizeFilter.CatmullRom);
var webp = pipe.Encode(ImageFormat.WebP);

// Sharpened grayscale scan
pipe.Resize(2048, 2048)
    .Grayscale()
    .Sharpen(sigma: 1.5f, threshold: 2);
var png = pipe.Encode(ImageFormat.Png);

// Rotate + crop + re-encode
pipe.Rotate(90)
    .Crop(x: 100, y: 200, width: 600, height: 400);
var jpeg = pipe.Encode(ImageFormat.Jpeg, quality: 90);
```

Operations apply in order. Ordering matters: `Crop → Resize` keeps the crop, then scales; `Resize → Crop` crops the resized image.

---

## One-liner helpers: `ImageConverter`

Static wrappers that open a pipeline, do one operation, encode, and return `byte[]`. Each has a sync and an `…Async` (thread-pool-offloaded, cancellable) form.

```csharp
byte[] thumb = ImageConverter.Thumbnail(bytes, maxWidth: 400, maxHeight: 400, format: ImageFormat.Jpeg, quality: 90);
byte[] webp  = ImageConverter.Convert(bytes, ImageFormat.WebP);
byte[] fixed = ImageConverter.Resize(bytes, 1024, 768);
byte[] clean = ImageConverter.StripExif(bytes);   // re-encode to drop metadata

byte[] thumb2 = await ImageConverter.ThumbnailAsync(bytes, 400, 400, ct: ct);
```

`StripExif(image, quality = 95, options)` removes metadata by decoding and re-encoding in the original format (lossy for JPEG, so use high quality). Every method takes an optional `ImagingOptions`.

---

## Batch processing: `ImageBatch`

`ImageBatch` is **static**; there is no instance to construct. Inputs are `IReadOnlyList<byte[]>`.

```csharp
byte[][] thumbs    = await ImageBatch.ThumbnailsAsync(images, 256, 256, ImageFormat.WebP, maxConcurrency: 4);
byte[][] converted = await ImageBatch.ConvertAsync(images, ImageFormat.WebP, quality: 80, maxConcurrency: 4);

byte[][] custom = await ImageBatch.ProcessAsync(images, data =>
{
    using var pipe = ImagePipeline.Load(data.Span);
    pipe.Grayscale().Thumbnail(200, 200);
    return pipe.Encode(ImageFormat.Png);
}, maxConcurrency: 4);
```

`maxConcurrency: 0` defaults to `Environment.ProcessorCount / 2`. A `SemaphoreSlim` caps how many pipelines are alive at once, so you don't exhaust native memory.

**Failure semantics.** Each item runs independently. A failed item is wrapped in `ImageBatchItemException`, which carries the input `Index`. Because the result is awaited via `Task.WhenAll`, awaiting the batch **re-throws the first** failed item's `ImageBatchItemException`. If you need every failure, don't rely on the throw: process items in smaller batches, or wrap your per-item operation to capture its own result/error.

---

## What's not supported

- Arbitrary-angle rotation
- Text overlay / drawing
- Colour-space conversion (sRGB assumed throughout)
- Animation frames (GIF / APNG / animated WebP: first frame only)
- EXIF-preserving operations (EXIF is stripped on re-encode)
- WebP/AVIF lossy quality control (upstream `image` 0.25 limitation)
- Floating-point / HDR pixel formats

For any of the above, use ImageSharp or a dedicated tool.

---

## Related

- [concepts.md](/modules/imaging/concepts/): pipeline lifecycle, thread safety, error model
- [native-deployment.md](/modules/imaging/native-deployment/): deploying the native binary
