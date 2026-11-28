# Common Mistakes

Patterns that look reasonable but fight the module's design.

---

## 1. Forgetting to dispose the pipeline

`ImagePipeline` owns its pixels in native memory. Not disposing holds that memory until the finaliser runs, out of the pool the next image would reuse.

```csharp
// ❌
var pipe = ImagePipeline.Load(bytes);
pipe.Resize(400, 400);
var webp = pipe.Encode(ImageFormat.WebP);
// pipe never disposed: leaks until GC

// ✅
using var pipe = ImagePipeline.Load(bytes);
pipe.Resize(400, 400);
var webp = pipe.Encode(ImageFormat.WebP);
```

Prefer `using`/`await using` even for single-operation flows.

---

## 2. Sharing a pipeline across threads

`ImagePipeline` is not thread-safe. Its pixel buffer is mutable state, and some operations replace it: concurrent access corrupts the image or uses a buffer another thread has already given back.

```csharp
// ❌ one pipeline, two tasks
using var pipe = ImagePipeline.Load(bytes);
await Task.WhenAll(
    Task.Run(() => pipe.Thumbnail(200, 200).Encode(ImageFormat.Png)),
    Task.Run(() => pipe.Thumbnail(400, 400).Encode(ImageFormat.Png)));

// ✅ one pipeline per task
await Task.WhenAll(
    Task.Run(() => { using var p = ImagePipeline.Load(bytes); p.Thumbnail(200, 200); return p.Encode(ImageFormat.Png); }),
    Task.Run(() => { using var p = ImagePipeline.Load(bytes); p.Thumbnail(400, 400); return p.Encode(ImageFormat.Png); }));
```

For bounded concurrency over many inputs, use `ImageBatch` (static).

---

## 3. Processing untrusted uploads without `ImagingOptions`

Decompression-bomb attacks (a 20 KB file that decodes to 50,000×50,000) and unexpected formats are common on public endpoints. The defaults are conservative but not tailored to adversarial input.

```csharp
// ❌
using var pipe = ImagePipeline.Load(userUpload);

// ✅
using var pipe = ImagePipeline.Load(userUpload, new ImagingOptions
{
    MaxInputBytes  = 20 * 1024 * 1024,
    MaxMegapixels  = 25,
    MaxWidth       = 4096,
    MaxHeight      = 4096,
    AllowedFormats = [ImageFormat.Jpeg, ImageFormat.Png, ImageFormat.WebP],
});
```

Or start from `ImagingOptions.Strict` and add `MaxWidth`/`MaxHeight`/`AllowedFormats`.

---

## 4. Using `Resize` when `Thumbnail` is what you want

`Resize(width, height)` forces exact dimensions and **stretches** if the ratio changes. `Thumbnail(maxW, maxH)` fits into the box and preserves aspect.

```csharp
// ❌ 1000×500 stretched into 400×400 → squashed
pipe.Resize(400, 400);

// ✅ 1000×500 fits into 400×400 as 400×200
pipe.Thumbnail(400, 400);
```

Only use `Resize(w, h)` when you deliberately want to force dimensions.

---

## 5. Trying to rotate by non-90° angles

Only the lossless quarter turns are supported (90, 180, 270).

```csharp
// ❌
pipe.Rotate(45);   // throws ArgumentException

// ✅
pipe.Rotate(90);
```

Arbitrary-angle rotation needs resampling and antialiasing; if you need it, use a full imaging library.

---

## 6. Expecting EXIF metadata to survive re-encoding, or rotating by the orientation tag yourself

An encode writes no EXIF: camera, GPS and timestamps are gone from any image a pipeline writes.

- For **thumbnails / resized versions** this is usually what you want (metadata stripped for privacy)
- For **original-quality re-saves** you'd lose camera / GPS data; to drop metadata without touching a JPEG's or PNG's pixels, use `ImageConverter.StripExif`, which copies the file without it

The **orientation** is not lost: `ImagePipeline.Load` applies a JPEG's or PNG's EXIF orientation to the pixels, so the image is already upright when you operate on it. Reading the tag and calling `Rotate` yourself turns it a second time.

```csharp
// ❌ the decoder has already applied orientation 6
using var pipe = ImagePipeline.Load(phonePhoto);
pipe.Rotate(90);

// ✅
using var pipe = ImagePipeline.Load(phonePhoto);   // upright, Width/Height as shown
```

Pragmatic.Imaging does not carry other EXIF through re-encoding. If you need it, use a dedicated metadata tool.

---

## 7. Expecting the `quality` argument to affect PNG or WebP

The `quality` of `Encode` applies to **JPEG only**.

- **PNG / BMP / TIFF / GIF** are lossless or paletted: `quality` is ignored.
- **WebP** encodes lossless through `Encode`, whatever the quality; **AVIF** uses the library default (an `image` 0.25 limitation).

```csharp
pipe.Encode(ImageFormat.Png, quality: 50);                 // quality ignored, normal PNG
pipe.Encode(ImageFormat.Jpeg, quality: 75);                // quality honoured
pipe.Encode(ImageFormat.WebP, quality: 75);                // quality ignored, lossless WebP
pipe.EncodeWebP(WebPEncoding.Lossy, quality: 75);          // quality honoured, lossy WebP
```

For size-constrained lossy output, use JPEG or lossy WebP.

---

## 8. Expecting `Encode` to consume the pipeline

`Encode`/`EncodeTo` do **not** dispose the pipeline. You can encode several formats from one pipeline state, or keep transforming.

```csharp
using var pipe = ImagePipeline.Load(bytes);
pipe.Thumbnail(400, 400);
var webp = pipe.Encode(ImageFormat.WebP);   // ✅ still valid afterwards
var png  = pipe.Encode(ImageFormat.Png);
```

The pixel buffer goes back to the pool only on `Dispose` (i.e. at the end of the `using`).

---

## 9. Loading a `Stream` then expecting to reuse it

`ImagePipeline.Load(Stream)` reads the whole stream. After load, the position is at the end.

```csharp
using var fs = File.OpenRead("photo.jpg");
using var pipe = ImagePipeline.Load(fs);
// fs is now at EOF: re-reading needs fs.Position = 0
```

If you need to inspect first, call `ImageInfo.FromStream` (it reads only the header), then rewind before `Load`.

---

## 10. Assuming `Thumbnail` upscales small sources

By default `Thumbnail` never enlarges: a source already smaller than the box comes back unchanged.

```csharp
// Source is 300×200
pipe.Thumbnail(400, 400);                    // stays 300×200
pipe.Thumbnail(400, 400, allowUpscale: true); // 400×267
```

Pass `allowUpscale: true` to enlarge, or enforce a minimum with `Resize`.

---

## 11. Expecting QR codes to embed logos or custom colours

`QrCode.GeneratePng` produces a black/white QR with configurable module size and margin: no colour, no logo, no ECC tuning. For richer output, use a dedicated library.

---

## 12. Running on Alpine Linux

The native binary is built against glibc; Alpine uses musl. PNG, JPEG, WebP, BMP, GIF, the operations and QR codes are managed and work there; the first AVIF or TIFF load or encode fails with `DllNotFoundException`. For those formats, use a Debian/Ubuntu base image, or build the native library for musl yourself (non-trivial). See [native-deployment.md](native-deployment.md).

---

## 13. Expecting cancellation to interrupt a decode or an operation

A decode, an encode or an operation runs to completion once started: none of them checks a token midway. `CancellationToken` is checked **between** steps (before/after each call), not during one. For long batches, use `ImageBatch` with `maxConcurrency` to cap work in flight.

---

## Related

- [troubleshooting.md](troubleshooting.md): runtime errors
- [concepts.md](concepts.md): why the API is shaped this way
