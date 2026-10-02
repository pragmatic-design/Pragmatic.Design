# Common Mistakes

Patterns that look reasonable but fight the module's design.

---

## 1. Forgetting to dispose the pipeline

`ImagePipeline` owns a native buffer. Not disposing leaks native memory until the finaliser runs.

```csharp
// ❌
var pipe = ImagePipeline.Load(bytes);
pipe.Resize(400, 400);
var webp = pipe.Encode(ImageFormat.WebP);
// pipe never disposed — leaks until GC

// ✅
using var pipe = ImagePipeline.Load(bytes);
pipe.Resize(400, 400);
var webp = pipe.Encode(ImageFormat.WebP);
```

Prefer `using`/`await using` even for single-operation flows.

---

## 2. Sharing a pipeline across threads

`ImagePipeline` is not thread-safe. The native buffer is mutable state; concurrent access corrupts it or panics the Rust code.

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

The native layer only supports lossless quarter-turn rotations (90, 180, 270).

```csharp
// ❌
pipe.Rotate(45);   // throws ArgumentException

// ✅
pipe.Rotate(90);
```

Arbitrary-angle rotation needs resampling and antialiasing; if you need it, use a full imaging library.

---

## 6. Expecting EXIF metadata to survive re-encoding

Every transformation decodes and re-encodes. EXIF is dropped as a side effect (this is also how `ImageConverter.StripExif` works).

- For **thumbnails / resized versions** this is usually what you want (metadata stripped for privacy)
- For **original-quality re-saves** you'd lose camera / GPS / orientation data

Pragmatic.Imaging does not preserve EXIF through re-encoding. If you need it, use a dedicated metadata tool.

---

## 7. Expecting the `quality` argument to affect PNG or WebP

`quality` applies to **JPEG only**.

- **PNG / BMP / TIFF / GIF** are lossless or paletted — `quality` is ignored.
- **WebP** encodes lossless and **AVIF** uses the library default — `quality` is ignored (an `image` 0.25 limitation).

```csharp
pipe.Encode(ImageFormat.Png, quality: 50);    // quality ignored — normal PNG
pipe.Encode(ImageFormat.Jpeg, quality: 75);   // quality honoured
```

For size-constrained lossy output, use JPEG.

---

## 8. Expecting `Encode` to consume the pipeline

`Encode`/`EncodeTo` do **not** dispose the pipeline. You can encode several formats from one pipeline state, or keep transforming.

```csharp
using var pipe = ImagePipeline.Load(bytes);
pipe.Thumbnail(400, 400);
var webp = pipe.Encode(ImageFormat.WebP);   // ✅ still valid afterwards
var png  = pipe.Encode(ImageFormat.Png);
```

The native buffer is released only on `Dispose` (i.e. at the end of the `using`).

---

## 9. Loading a `Stream` then expecting to reuse it

`ImagePipeline.Load(Stream)` reads the whole stream. After load, the position is at the end.

```csharp
using var fs = File.OpenRead("photo.jpg");
using var pipe = ImagePipeline.Load(fs);
// fs is now at EOF — re-reading needs fs.Position = 0
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

`QrCode.GeneratePng` produces a black/white QR with configurable module size and margin — no colour, no logo, no ECC tuning. For richer output, use a dedicated library.

---

## 12. Running on Alpine Linux

The native binary is built against glibc; Alpine uses musl. The app fails at the first image operation with `DllNotFoundException`. Use a Debian/Ubuntu base image, or build the native library for musl yourself (non-trivial). See [native-deployment.md](native-deployment.md).

---

## 13. Expecting cancellation to interrupt a native call

The native functions run to completion once invoked — there's no cancellation cooperation on the Rust side. `CancellationToken` is checked **between** operations (before/after each native call), not during one. For long batches, use `ImageBatch` with `maxConcurrency` to cap work in flight.

---

## Related

- [troubleshooting.md](troubleshooting.md) — runtime errors
- [concepts.md](concepts.md) — why the API is shaped this way
