using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Barcodes;
using Pragmatic.Imaging.Codecs.Jpeg;
using Pragmatic.Imaging.Codecs.Png;
using Pragmatic.Imaging.Codecs.Transforms;
using ImageConverter = Pragmatic.Imaging.ImageConverter;
using ImageFormat = Pragmatic.Imaging.ImageFormat;
using ImageInfo = Pragmatic.Imaging.ImageInfo;
using ImagePipeline = Pragmatic.Imaging.ImagePipeline;
using ImagingQrCode = Pragmatic.Imaging.QrCode;

// The upload path of a server, Native AOT: encode a photo-sized image as JPEG, decode it at the scale that
// covers a thumbnail, resize to the thumbnail, encode that as PNG and read it back pixel for pixel; and a
// QR code. Then the same through Pragmatic.Imaging's public API, with no native library next to the
// binary. Every check prints ok or FAIL; the last line is AOT-CODECS-OK only when all of them pass.
var failures = 0;

void Check(bool condition, string what)
{
    Console.WriteLine($"{(condition ? "ok  " : "FAIL")} {what}");
    if (!condition)
        failures++;
}

// x86-64-v3 from the package's targets: without it ILC drops every AVX2 path at compile time.
if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
    Check(Avx2.IsSupported, $"AVX2 paths compiled in (Avx2.IsSupported={Avx2.IsSupported})");

const int width = 2048;
const int height = 1536;
using var photo = new PixelBuffer(width, height);
var pixels = photo.Pixels;
for (var y = 0; y < height; y++)
{
    for (var x = 0; x < width; x++)
    {
        var i = (y * width + x) * 4;
        pixels[i] = (byte)(x * 255 / width);
        pixels[i + 1] = (byte)(y * 255 / height);
        pixels[i + 2] = (byte)((x ^ y) & 0xFF);
        pixels[i + 3] = 255;
    }
}

using var jpegStream = new MemoryStream();
JpegEncoder.Write(photo, jpegStream, 85);
var jpeg = jpegStream.ToArray();
Check(jpeg.Length > 0 && jpeg[0] == 0xFF && jpeg[1] == 0xD8, $"JPEG encoded ({jpeg.Length} bytes)");

var (sourceWidth, sourceHeight) = JpegDecoder.Identify(jpeg);
Check(sourceWidth == width && sourceHeight == height, $"JPEG identified as {sourceWidth}x{sourceHeight}");

var (thumbWidth, thumbHeight) = FitBox.Fit(sourceWidth, sourceHeight, 400, 300);
var scale = JpegDecoder.ScaleToCover(sourceWidth, sourceHeight, thumbWidth, thumbHeight);
using var reduced = JpegDecoder.Decode(jpeg, scale);
Check(reduced.Width < width, $"JPEG decoded at a reduced scale ({reduced.Width}x{reduced.Height})");

using var thumbnail = Resampler.Resize(reduced, thumbWidth, thumbHeight, ResizeFilter.Lanczos3);
Check(thumbnail.Width == thumbWidth && thumbnail.Height == thumbHeight, $"thumbnail {thumbnail.Width}x{thumbnail.Height}");

using var pngStream = new MemoryStream();
PngEncoder.Write(thumbnail, pngStream);
using var roundTrip = PngDecoder.Decode(pngStream.ToArray());
Check(roundTrip.Pixels.SequenceEqual(thumbnail.Pixels), "PNG round trip is lossless");

var qr = QrCode.Encode("https://pragmaticdesign.net");
Check(qr.Version == 3 && qr.Size == 29, $"QR code version {qr.Version}, {qr.Size} modules");

// Pragmatic.Imaging: PNG, JPEG and every operation are managed, so they run without its native library,
// which the smoke script deletes from the publish output before running this.
string[] nativeNames = ["Pragmatic.Imaging.Native.dll", "libPragmatic.Imaging.Native.so", "libPragmatic.Imaging.Native.dylib", "libpragmatic_imaging_native.so"];
Check(!nativeNames.Any(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name))), "no native imaging library in the publish output");

using (var pipeline = ImagePipeline.Load(jpeg))
{
    pipeline.Thumbnail(400, 300).Rotate(90).Grayscale().Blur(1.5f).Sharpen().Crop(10, 10, 200, 300);
    var info = ImageInfo.FromBytes(pipeline.Encode(ImageFormat.Png));
    Check(info is { Width: 200, Height: 300, Format: ImageFormat.Png }, $"pipeline thumbnail, rotate, filters, crop: {info.Width}x{info.Height} {info.Format}");
}

var stripped = ImageConverter.StripExif(jpeg);
Check(stripped.AsSpan().SequenceEqual(jpeg), "StripExif on a JPEG without metadata changes no byte");

var qrPng = ImagingQrCode.GeneratePng("https://pragmaticdesign.net", moduleSize: 4, margin: 4);
var qrInfo = ImageInfo.FromBytes(qrPng);
Check(qrInfo.Width == (29 + 8) * 4, $"QR code PNG {qrInfo.Width}x{qrInfo.Height}");

Console.WriteLine(failures == 0 ? "AOT-CODECS-OK" : $"AOT-CODECS-FAILED ({failures})");
return failures == 0 ? 0 : 1;
