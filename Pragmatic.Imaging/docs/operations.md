# Operations Reference

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

An image with any translucent pixel is resampled with **premultiplied alpha** (colour times alpha, divided back after), so a transparent neighbour does not darken an edge: the dark fringe around a resized transparent PNG does not appear. An opaque image takes the straight path, where premultiplying would change nothing, and gives the native library's result. The same holds for `Thumbnail`.

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

Rotate by 90, 180, or 270 degrees (positive = clockwise). Arbitrary angles are **not supported**: only the lossless quarter turns, which move pixels without resampling them.

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

Convert to luminance (ITU-R BT.601). The pixel format stays what it was (pixels just have R=G=B; a grey image is left as it is); the alpha channel is preserved.

```csharp
pipe.Grayscale();
```

### `Blur(sigma)`

Gaussian blur. `sigma` is the standard deviation in pixels; `1.0` is light, `5.0` is heavy. `0` means the default of `0.8`; a negative, NaN or infinite sigma throws `ArgumentOutOfRangeException`. Edges are extended by repeating the border pixels, and alpha is blurred like a colour.

```csharp
pipe.Blur(sigma: 2.0f);
```

### `Sharpen(sigma, threshold)`

Unsharp-mask sharpening. `sigma` (default `1.0`, must be positive) controls the underlying blur radius; `threshold` (default `1`) is the minimum channel difference that triggers sharpening. Every channel is sharpened, alpha included.

```csharp
pipe.Sharpen();                              // sigma 1.0, threshold 1
pipe.Sharpen(sigma: 1.5f, threshold: 3);     // stronger, ignores micro-noise
```

### `Brightness(value)`

Add a constant to red, green and blue. Range `-255` to `+255`; each channel is clamped to 0–255, and alpha is unchanged.

```csharp
pipe.Brightness(20);       // brighten
pipe.Brightness(-30);      // darken
```

### `Contrast(value)`

Contrast adjustment: each channel's distance from mid-grey is scaled by `((100 + value) / 100)²`. `0` is identity; positive values increase contrast, negative flatten. Typical range `-100` to `+100`. Alpha is scaled too, as the native library did.

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

`quality` applies to **JPEG only**. WebP is written lossless, AVIF uses the library default, and lossless/paletted formats ignore it. Supported output formats: PNG, JPEG, WebP, AVIF, GIF (first frame), BMP, TIFF.

### `EncodeWebP(encoding, quality)` / `EncodeWebPTo(stream, encoding, quality)`

WebP, lossless or lossy by name. `WebPEncoding.Lossless` writes what `Encode(ImageFormat.WebP)` writes; `WebPEncoding.Lossy` writes a VP8 frame at `quality`, 0 to 100 on libwebp's scale (default 75), with an image's alpha kept exactly in an `ALPH` chunk beside it.

```csharp
byte[] webp = pipe.EncodeWebP(WebPEncoding.Lossy, quality: 80);
pipe.EncodeWebPTo(output, WebPEncoding.Lossy);
```

The lossy file matches libwebp's at the effort SkiaSharp runs it (method 3): on the reference photographs, SSIM within 0.001 at 50, 75 and 90 and files within 0.2% of its size. `ImageConverter` and `ImageBatch` take an `ImageFormat`, and so write WebP lossless; for lossy, encode from a pipeline.

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

The text is encoded as UTF-8 in byte mode, at error-correction level Medium, in the smallest version that holds it: up to 2,331 bytes (version 40). Longer text throws `ArgumentException`; so does an image side over 8,192 pixels (`ImagingException`). Output is always PNG, black on white: no colour, logo, or ECC tuning. For richer QR output, use a dedicated library.

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
byte[] clean = ImageConverter.StripExif(bytes);   // drop EXIF and XMP, keep the pixels

byte[] thumb2 = await ImageConverter.ThumbnailAsync(bytes, 400, 400, ct: ct);
```

`StripExif(image, quality = 95, options)` removes EXIF and XMP. A JPEG or PNG is not decoded: the file is copied without its APP1 segments (JPEG) or eXIf chunks (PNG), so a JPEG keeps its exact compressed data. The orientation tag is kept, alone in a new EXIF block, so the photo still shows upright; camera, GPS and timestamps go, while colour profiles, comments, IPTC and PNG text chunks stay. Other formats are decoded and re-encoded in the same format, and `quality` applies only there. Every method takes an optional `ImagingOptions`.

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

`maxConcurrency: 0` defaults to `Environment.ProcessorCount / 2`. A `SemaphoreSlim` caps how many pipelines are alive at once, so you don't exhaust the native memory their pixels live in.

**Failure semantics.** Each item runs independently. A failed item is wrapped in `ImageBatchItemException`, which carries the input `Index`. Because the result is awaited via `Task.WhenAll`, awaiting the batch **re-throws the first** failed item's `ImageBatchItemException`. If you need every failure, don't rely on the throw: process items in smaller batches, or wrap your per-item operation to capture its own result/error.

---

## What's not supported

- Arbitrary-angle rotation
- Text overlay / drawing
- Colour-space conversion (sRGB assumed throughout)
- Animation frames (GIF / APNG / animated WebP: first frame only)
- EXIF-preserving operations (an encode writes no EXIF; the orientation is applied to the pixels on load, so nothing is lost by it)
- AVIF quality control (upstream `image` 0.25 limitation)
- Floating-point / HDR pixel formats

For any of the above, use ImageSharp or a dedicated tool.

---

## Related

- [concepts.md](concepts.md): pipeline lifecycle, thread safety, error model
- [native-deployment.md](native-deployment.md): deploying the native binary
